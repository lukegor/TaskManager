using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;
using Process = TaskManager.Domain.Models.Process;
using IDispatcherService = TaskManager.Abstractions.IDispatcherService;

namespace TaskManager.Presentation
{
    /// <summary>
    /// Single source of truth for the running-process list (presentation side).
    /// Storage: ObservableCollection wrapped read-only + PID index + once-per-PID enrichment cache.
    /// Pipeline per tick: cheap snapshot → off-thread diff/enrich (pure Domain) → ONE dispatcher
    /// batch applying removes/adds/in-place updates. Polling = one cancellable PeriodicTimer loop.
    /// </summary>
    internal sealed class ProcessListCatalog : IProcessListCatalog
    {
        private readonly ObservableCollection<ProcessItem> _items = [];
        private readonly Dictionary<int, ProcessItem> _index = [];
        private readonly Dictionary<int, ProcessEnrichment> _enrichment = [];
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly object _pollingLock = new();
        private CancellationTokenSource? _pollingCts;

        private readonly IDispatcherService _dispatcher;
        private readonly ISystemProcessEnumerator _enumerator;
        private readonly ProcessEnricher _enricher;
        private readonly ISettingsService _settings;
        private readonly IProcessOperations _processOps;
        private readonly ILogger<ProcessListCatalog> _logger;

        public ProcessListCatalog(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
            ProcessEnricher enricher, ISettingsService settings, IProcessOperations processOps,
            ILogger<ProcessListCatalog> logger)
        {
            _dispatcher = dispatcher;
            _enumerator = enumerator;
            _enricher = enricher;
            _settings = settings;
            _processOps = processOps;
            _logger = logger;

            Items = new ReadOnlyObservableCollection<ProcessItem>(_items);
            _items.CollectionChanged += (_, _) => ProcessCount = _items.Count;
            _settings.Changed += OnSettingsChanged;
        }

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

        public async Task InitializeAsync()
        {
            await SafePollingRefreshAsync();
            StartPolling();
        }

        public async Task PerformRefreshAsync(bool isUserInitiated)
        {
            if (!isUserInitiated)
            {
                await SafePollingRefreshAsync();
                return;
            }

            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await RefreshCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _refreshGate.Release();
            }

            RestartPolling(); // manual refresh restarts the polling phase (old timer.Restart())
        }

        public IReadOnlyList<Process> SnapshotForExport()
        {
            lock (_index)
            {
                return _index.Values.Select(i => i.Process.DeepCopy()).ToList();
            }
        }

        public async Task<ProcessOpSummary> TerminateProcessesAsync(IReadOnlyCollection<int> pids)
        {
            // Deliberately NO ConfigureAwait(false) anywhere in these methods:
            // SetPriority's writeback mutates INPC-bound rows and must resume on the
            // caller's context (the UI thread when invoked from commands).
            return await Task.Run(() => _processOps.TerminateProcesses(pids));
        }

        public async Task<ProcessOpSummary> SetPriorityAsync(IReadOnlyCollection<int> pids, ProcessPriorityClass priority)
        {
            var summary = await Task.Run(() => _processOps.SetPriority(pids, priority));

            foreach (var pid in summary.SucceededPids)
            {
                WritebackPriority(pid, ProcessBasePriority.Get(priority));
            }

            return summary;
        }

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

        // ---- polling ----

        private void OnSettingsChanged(AppSettings settings)
        {
            var seconds = RefreshFrequencies.SecondsMapping[settings.ProcessesRefreshFrequency];
            _logger.LogInformation("Polling interval set to {Seconds}s", seconds);
            RestartPolling(); // picks up new period; Paused stops ticking; resume starts a fresh loop
        }

        public void StartPolling()
        {
            lock (_pollingLock)
            {
                if (_pollingCts is not null)
                {
                    return;
                }

                _pollingCts = new CancellationTokenSource();
                _ = RunPollingLoopAsync(_pollingCts.Token);
            }
        }

        private void RestartPolling()
        {
            lock (_pollingLock)
            {
                _pollingCts?.Cancel();
                _pollingCts = new CancellationTokenSource();
                _ = RunPollingLoopAsync(_pollingCts.Token);
            }
        }

        internal int CurrentIntervalSeconds => RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency];

        private async Task RunPollingLoopAsync(CancellationToken ct)
        {
            try
            {
                var seconds = CurrentIntervalSeconds;
                if (seconds == 0)
                {
                    return; // paused: idle until the next settings change starts a fresh loop
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    await SafePollingRefreshAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // normal reconfiguration/shutdown path
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling loop terminated unexpectedly");
            }
        }

        /// <remarks>
        /// Polling skips a tick when a refresh is already in flight; manual refresh waits.
        /// Escaping exceptions are logged, never fatal.
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
                await RefreshCoreAsync();
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

        /// <summary>Test convenience alias for the non-polling initial fill.</summary>
        internal Task LoadForTestAsync() => SafePollingRefreshAsync();

        // ---- refresh pipeline ----

        private async Task RefreshCoreAsync()
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
        /// Workers read _index/_enrichment under lock(_index); _items is touched by
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
                        var process = Process.FromSnapshot(added, enrichedNew[added.Pid]);
                        _enrichment[added.Pid] = enrichedNew[added.Pid];
                        var item = new ProcessItem(process);
                        _index[added.Pid] = item;
                        _items.Add(item);
                    }

                    foreach (var upd in diff.Updated)
                    {
                        if (_index.TryGetValue(upd.Pid, out var item))
                        {
                            item.Process.ApplySnapshot(upd);
                        }
                    }
                }
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
