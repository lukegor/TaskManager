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
    internal sealed class ProcessListCatalog : IProcessListCatalog, IDisposable
    {
        private readonly ObservableCollection<ProcessItem> _items = [];
        private readonly Dictionary<int, ProcessItem> _index = [];
        private readonly Dictionary<int, ProcessEnrichment> _enrichment = [];
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly object _pollingLock = new();
        private CancellationTokenSource? _pollingCts;
        private Task? _pollingLoop;
        private volatile bool _disposed;

        private readonly IDispatcherService _dispatcher;
        private readonly ISystemProcessEnumerator _enumerator;
        private readonly ProcessEnricher _enricher;
        private readonly ISettingsService _settings;
        private readonly IProcessOperations _processOps;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ProcessListCatalog> _logger;

        public ProcessListCatalog(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
            ProcessEnricher enricher, ISettingsService settings, IProcessOperations processOps,
            TimeProvider timeProvider, ILogger<ProcessListCatalog> logger)
        {
            _dispatcher = dispatcher;
            _enumerator = enumerator;
            _enricher = enricher;
            _settings = settings;
            _processOps = processOps;
            _timeProvider = timeProvider;
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

        private double _lastCompletedDurationMs;

        private RefreshDiagnostics? _lastRefresh;
        public RefreshDiagnostics? LastRefresh
        {
            get => _lastRefresh;
            private set
            {
                if (!Equals(_lastRefresh, value))
                {
                    _lastRefresh = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPollingPaused = true; // nothing flows until polling is configured
        public bool IsPollingPaused
        {
            get => _isPollingPaused;
            private set
            {
                if (_isPollingPaused != value)
                {
                    _isPollingPaused = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Best-effort telemetry: diagnostics must never destabilize the pipeline.
        /// A dispatcher failure during ApplyBatch lands here from the warning-catch;
        /// re-throwing would escape SafePollingRefreshAsync's containment.
        /// </summary>
        private void PublishRefresh(double durationMs, RefreshOutcome outcome)
        {
            try
            {
                _dispatcher.Invoke(() =>
                {
                    if (outcome == RefreshOutcome.Ok)
                    {
                        _lastCompletedDurationMs = durationMs;
                    }

                    LastRefresh = new RefreshDiagnostics(durationMs, outcome, _timeProvider.GetLocalNow());
                });
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(ex, "Failed to publish refresh diagnostics");
                }
            }
        }

        public async Task InitializeAsync()
        {
            await SafePollingRefreshAsync();
            StartPolling();
            IsPollingPaused = CurrentIntervalSeconds == 0;
        }

        public async Task PerformRefreshAsync(bool isUserInitiated)
        {
            if (!isUserInitiated)
            {
                await SafePollingRefreshAsync();
                return;
            }

            try
            {
                await _refreshGate.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return; // shutdown won the race; nothing left to refresh
            }

            try
            {
                await RefreshCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                TryReleaseGate();
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
        private void WritebackPriority(int pid, int newPriority)
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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Polling interval set to {Seconds}s", seconds);
            }
            RestartPolling(); // picks up new period; Paused stops ticking; resume starts a fresh loop
            IsPollingPaused = seconds == 0;
        }

        public void StartPolling()
        {
            lock (_pollingLock)
            {
                if (_disposed || _pollingCts is not null)
                {
                    return;
                }

                _pollingCts = new CancellationTokenSource();
                _pollingLoop = RunPollingLoopAsync(_pollingCts.Token);
            }
        }

        private void RestartPolling()
        {
            lock (_pollingLock)
            {
                if (_disposed)
                {
                    return;
                }

                _pollingCts?.Cancel();
                _pollingCts = new CancellationTokenSource();
                _pollingLoop = RunPollingLoopAsync(_pollingCts.Token);
            }
        }

        internal int CurrentIntervalSeconds => RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency];

        private async Task RunPollingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var seconds = CurrentIntervalSeconds;
                    if (seconds == 0)
                    {
                        return; // paused: idle until the next settings change starts a fresh loop
                    }

                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), _timeProvider);
                    while (await timer.WaitForNextTickAsync(ct))
                    {
                        await SafePollingRefreshAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    return; // normal reconfiguration/shutdown path
                }
                catch (Exception ex)
                {
                    // defense in depth: a single unexpected failure must not kill polling
                    // until the next settings change. Real-time backoff on purpose.
                    _logger.LogError(ex, "Polling iteration failed; restarting loop");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
        }

        /// <remarks>
        /// Polling skips a tick when a refresh is already in flight; manual refresh waits.
        /// Escaping exceptions are logged, never fatal.
        /// </remarks>
        internal async Task SafePollingRefreshAsync()
        {
            try
            {
                if (!_refreshGate.Wait(0))
                {
                    _logger.LogDebug("Refresh skipped; previous refresh still in flight");
                    PublishRefresh(_lastCompletedDurationMs, RefreshOutcome.Skipped);
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return; // shutdown raced this tick
            }

            try
            {
                await RefreshCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Polling refresh failed");
                PublishRefresh(_lastCompletedDurationMs, RefreshOutcome.Failed);
            }
            finally
            {
                TryReleaseGate();
            }
        }

        /// <summary>
        /// A refresh finishing after Dispose disposed the gate loses the race by design;
        /// the release must stay silent instead of surfacing as shutdown noise.
        /// </summary>
        private void TryReleaseGate()
        {
            try
            {
                _refreshGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>Test convenience alias for the non-polling initial fill.</summary>
        internal Task LoadForTestAsync() => SafePollingRefreshAsync();

        // ---- refresh pipeline ----

        private async Task RefreshCoreAsync()
        {
            var startTimestamp = _timeProvider.GetTimestamp();
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

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                var diff = batch.Batch;
                _logger.LogDebug(
                    "Refresh completed in {ElapsedMs:F1} ms: {AddedCount} added, {RemovedCount} removed, {UpdatedCount} updated",
                    elapsed.TotalMilliseconds, diff.Added.Count, diff.Removed.Count, diff.Updated.Count);
            }

            ApplyBatch(batch.Batch, batch.Enrichments);

            var totalElapsed = _timeProvider.GetElapsedTime(startTimestamp);
            PublishRefresh(totalElapsed.TotalMilliseconds, RefreshOutcome.Ok);
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

        /// <summary>
        /// Terminal shutdown disposal. Order matters: flag + cancel under the polling lock
        /// (so no new loop can start and RestartPolling becomes a no-op), then observe the
        /// loop task outside the lock so it cannot touch the gate after it is disposed.
        /// Idempotent; the gate is disposed last.
        /// </summary>
        public void Dispose()
        {
            Task? loop;

            lock (_pollingLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pollingCts?.Cancel();
                loop = _pollingLoop;
            }

            // Bounded wait outside the lock: a mid-refresh loop finishes its tick before we
            // tear down the gate; if it hangs past the bound, TryReleaseGate absorbs the fallout.
            try
            {
                loop?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // canceled/faulted outcome must not break disposal
            }

            _settings.Changed -= OnSettingsChanged;
            _pollingCts?.Dispose();
            _refreshGate.Dispose(); // last: nothing may enter or release it afterwards
        }
    }
}
