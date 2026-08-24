using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;

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
            var seconds = RefreshFrequencies.SecondsMapping[settings.ProcessesRefreshFrequency];
            _timer.UpdatePolling(seconds);
            _logger.LogInformation("Polling interval updated to {Seconds}s", seconds);
        }

        /// <summary>Initial fill; identical pipeline to a polling tick.</summary>
        public async Task LoadProcesses() => await RunRefreshAsync(manual: false);

        /// <summary>Starts the polling timer; composition flow calls this once at startup.</summary>
        public void StartPollingProcesses()
        {
            _timer.Start();
        }

        /// <summary>Runs a refresh; user-initiated runs queue behind any in-flight refresh and restart the polling timer afterwards.</summary>
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
                List<ProcessSnapshot> toProbe;
                lock (_index)
                {
                    toProbe = [.. diff.Added.Where(a => !_enrichment.ContainsKey(a.Pid))];
                    foreach (var added in diff.Added.Where(a => _enrichment.ContainsKey(a.Pid)))
                    {
                        enrichments[added.Pid] = _enrichment[added.Pid];
                    }
                }

                foreach (var added in toProbe)
                {
                    enrichments[added.Pid] = _enricher.TryEnrich(added.Pid, out var e)
                        ? e
                        : new ProcessEnrichment(string.Empty, ArchitectureType.Unknown);
                }

                return new PipelineBatch(diff, enrichments);
            }).ConfigureAwait(false);

            ApplyBatch(batch.Batch, batch.Enrichments);
        }

        private readonly record struct PipelineBatch(ProcessListDiff Batch, Dictionary<int, ProcessEnrichment> Enrichments);

        /// <summary>
        /// The only mutation point of _items/_index/_enrichment on the UI thread.
        /// SetPriority's priority writeback is the sole off-gate mutation (UI-thread command handler);
        /// workers read _index/_enrichment under lock(_index); _items is touched by
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

        /// <summary>Materialized deep copy of current rows; safe to hold across refreshes.</summary>
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

        /// <summary>Updates priority in the index after a successful OS operation.</summary>
        public void WritebackPriority(int pid, int newPriority)
        {
            lock (_index)
            {
                if (_index.TryGetValue(pid, out var item))
                {
                    item.Process.Priority = newPriority;
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
