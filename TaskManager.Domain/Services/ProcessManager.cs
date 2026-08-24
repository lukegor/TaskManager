using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services
{
    /// <summary>
    /// Single source of truth for the running-process list.
    /// Storage: ObservableCollection wrapped read-only + PID index + once-per-PID enrichment cache.
    /// Pipeline per tick: cheap snapshot → off-thread diff/enrich → ONE dispatcher batch applying
    /// removes/adds/in-place updates.
    /// </summary>
    public class ProcessManager : INotifyPropertyChanged
    {
        private readonly ObservableCollection<ProcessItem> _items = [];
        private readonly Dictionary<int, ProcessItem> _index = [];
        private readonly Dictionary<int, ProcessEnrichment> _enrichment = [];
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        public ReadOnlyObservableCollection<ProcessItem> Items { get; }

        private int _processCount;
        public int ProcessCount
        {
            get => _processCount;
            private set
            {
                if (_processCount != value)
                {
                    _processCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly IDispatcherService _dispatcher;
        private readonly ISystemProcessEnumerator _enumerator;
        private readonly ProcessEnricher _enricher;
        private readonly ISettingsService _settings;
        private readonly TimerManager _timer;
        private readonly ILogger<ProcessManager> _logger;

        public ProcessManager(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
            ProcessEnricher enricher, ISettingsService settings, TimerManager timerManager,
            ILogger<ProcessManager> logger)
        {
            _dispatcher = dispatcher;
            _enumerator = enumerator;
            _enricher = enricher;
            _settings = settings;
            _timer = timerManager;
            _logger = logger;

            Items = new ReadOnlyObservableCollection<ProcessItem>(_items);
            _items.CollectionChanged += (_, _) => ProcessCount = _items.Count;

            _timer.Elapsed += OnProcessPolling;
            _settings.Changed += OnSettingsChanged;
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            var seconds = RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[settings.ProcessesRefreshFrequency];
            _timer.UpdatePolling(seconds);
            _logger.LogInformation("Polling interval updated to {Seconds}s", seconds);
        }

        public async Task LoadProcesses() => await RunRefreshAsync(manual: false);

        public void StartPollingProcesses()
        {
            _timer.Start();
        }

        public async Task PerformRefresh(bool isUserInitiated)
        {
            await RunRefreshAsync(manual: isUserInitiated);

            if (isUserInitiated)
            {
                _timer.Restart();
            }
        }

        private async void OnProcessPolling(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await SafePollingRefreshAsync();
        }

        /// <remarks>
        /// async void callback boundary: an escaping exception would terminate the process.
        /// Polling skips a tick when a refresh is already in flight; manual refresh waits.
        /// </remarks>
        internal async Task SafePollingRefreshAsync()
        {
            if (!_refreshGate.Wait(0))
            {
                _logger.LogDebug("Refresh skipped; previous refresh still in flight");
                return;
            }

            try
            {
                await RunRefreshCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Polling refresh failed");
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task RunRefreshAsync(bool manual)
        {
            if (manual)
            {
                await _refreshGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await RunRefreshCoreAsync().ConfigureAwait(false);
                }
                finally
                {
                    _refreshGate.Release();
                }
            }
            else
            {
                await SafePollingRefreshAsync();
            }
        }

        private async Task RunRefreshCoreAsync()
        {
            IReadOnlyList<ProcessSnapshot> snapshot = await Task.Run(() => _enumerator.Capture()).ConfigureAwait(false);

            Dictionary<int, Process> current;
            lock (_index)
            {
                current = _index.ToDictionary(kv => kv.Key, kv => kv.Value.Process);
            }

            PipelineBatch batch = await Task.Run(() =>
            {
                var diff = ProcessDiffEngine.Compute(current, snapshot);
                var enrichments = new Dictionary<int, ProcessEnrichment>();
                lock (_index)
                {
                    foreach (var added in diff.Added)
                    {
                        if (!_enrichment.TryGetValue(added.Pid, out var e) && !_enricher.TryEnrich(added.Pid, out e))
                        {
                            e = new ProcessEnrichment(string.Empty, ArchitectureType.Unknown);
                        }
                        enrichments[added.Pid] = e;
                    }
                }

                return new PipelineBatch(diff, enrichments);
            }).ConfigureAwait(false);

            ApplyBatch(batch.Batch, batch.Enrichments);
        }

        private readonly record struct PipelineBatch(ProcessListDiff Batch, Dictionary<int, ProcessEnrichment> Enrichments);

        /// <summary>
        /// The ONLY mutation point of _items/_index/_enrichment. Runs on the UI thread.
        /// Worker threads read _index/_enrichment under lock(_index); _items is touched by
        /// no thread except the UI thread.
        /// </summary>
        private void ApplyBatch(ProcessListDiff diff, Dictionary<int, ProcessEnrichment> enrichedNew)
        {
            _dispatcher.Invoke(() =>
            {
                lock (_index)
                {
                    foreach (var pid in diff.Removed)
                    {
                        if (_index.Remove(pid, out var item))
                        {
                            _items.Remove(item);
                            _enrichment.Remove(pid);
                        }
                    }

                    foreach (var added in diff.Added)
                    {
                        var process = Materialize(added, enrichedNew[added.Pid]);
                        _enrichment[added.Pid] = enrichedNew[added.Pid];
                        var item = new ProcessItem(process);
                        _index[added.Pid] = item;
                        _items.Add(item);
                    }

                    foreach (var upd in diff.Updated)
                    {
                        if (_index.TryGetValue(upd.Pid, out var item))
                        {
                            ApplySnapshot(item.Process, upd);
                        }
                    }
                }
            });
        }

        private static Process Materialize(ProcessSnapshot s, ProcessEnrichment e) => new()
        {
            Name = s.Name,
            Pid = s.Pid,
            Path = e.Path,
            ArchitectureType = e.Architecture,
            Priority = s.BasePriority,
            ThreadCount = s.ThreadCount,
            Ppid = s.Ppid,
        };

        private static void ApplySnapshot(Process target, ProcessSnapshot s)
        {
            target.Name = s.Name;
            target.ThreadCount = s.ThreadCount;
            target.Priority = s.BasePriority;
            target.Ppid = s.Ppid;
        }

        public IReadOnlyList<Process> SnapshotForExport()
        {
            lock (_index)
            {
                return _index.Values.Select(i => CopyOf(i.Process)).ToList();
            }
        }

        private static Process CopyOf(Process p) => new()
        {
            Name = p.Name,
            Pid = p.Pid,
            Path = p.Path,
            ArchitectureType = p.ArchitectureType,
            Priority = p.Priority,
            ThreadCount = p.ThreadCount,
            Ppid = p.Ppid,
        };

        public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> selectedProcesses)
        {
            return ExecutePerPid(selectedProcesses, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
                _logger.LogDebug("Process {Pid} was terminated", pid);
            });
        }

        public ProcessOpSummary SetPriority(IReadOnlyCollection<int> selectedProcesses, System.Diagnostics.ProcessPriorityClass priority)
        {
            var summary = ExecutePerPid(selectedProcesses, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.PriorityClass = priority;
            });

            foreach (var pid in summary.SucceededPids)
            {
                lock (_index)
                {
                    if (_index.TryGetValue(pid, out var item))
                    {
                        item.Process.Priority = PriorityTypeHelper.GetBasePriority(priority);
                    }
                }
            }

            return summary;
        }

        private ProcessOpSummary ExecutePerPid(IEnumerable<int> selectedPids, Action<int> operation)
        {
            var succeeded = new List<int>();
            var failures = new List<ProcessOpFailure>();

            foreach (var pid in selectedPids)
            {
                try
                {
                    operation(pid);
                    succeeded.Add(pid);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    var reason = ClassifyFailure(ex);
                    _logger.LogWarning(ex, "Process operation failed for PID {Pid} ({Reason})", pid, reason);
                    failures.Add(new ProcessOpFailure(pid, reason));
                }
            }

            return new ProcessOpSummary { SucceededPids = succeeded, Failures = failures };
        }

        private const int ErrorAccessDenied = 5;

        private static ProcessOpFailureReason ClassifyFailure(Exception ex) => ex switch
        {
            Win32Exception { NativeErrorCode: ErrorAccessDenied } => ProcessOpFailureReason.AccessDenied,
            ArgumentException => ProcessOpFailureReason.ProcessExited,
            InvalidOperationException => ProcessOpFailureReason.ProcessExited,
            _ => ProcessOpFailureReason.Unknown
        };

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
