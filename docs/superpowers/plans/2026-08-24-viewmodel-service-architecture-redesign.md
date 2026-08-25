# ViewModel / Service Architecture Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move all presentation concerns (view state, dispatcher batching, polling, window orchestration) out of `TaskManager.Domain` into the app, and make every ViewModel constructible headless with fakes.

**Architecture:** `TaskManager.Domain` keeps pure logic only (enumeration, diff engine, enricher, exporters, `ProcessOperationsService`). The app gains `ProcessListCatalog` (view state + pipeline + `PeriodicTimer` polling), an `IWindowService` (all window/dialog construction), `IFolderPicker`, and an exporter factory delegate registered in the composition root. ViewModels depend only on interfaces; `IServiceProvider`, factories, `TimerManager`, `ProcessManager`, `ViewModelBase`, and `Preconditions` are deleted.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm (source generators), Microsoft.Extensions.DependencyInjection, PeriodicTimer (BCL), xunit.v3 + Shouldly + NSubstitute (+ Xunit.StaFact) for tests.

## Global Constraints

- Build: `dotnet build TaskManager.slnx` from repo root; SDK pinned via `global.json`.
- Hermetic suite (must always pass): `dotnet test tests/TaskManager.UnitTests`.
- Live-state suite (run explicitly): `dotnet test tests/TaskManager.IntegrationTests`.
- Only the WPF exe references WPF; Domain must contain no presentation types (`IDispatcherService`, `ObservableCollection`) — enforced in this redesign.
- Per-field INPC on `Domain/Models/Process` **stays** (decided in spec §1): DataGrid cell updates depend on it; `INotifyPropertyChanged` is BCL, not WPF.
- `src/TaskManager/TaskManager.csproj` has `InternalsVisibleTo("TaskManager.UnitTests")` and `"DynamicProxyGenAssembly2"` — internal types are constructible/substitutable in tests.
- Commit style follows repo history: `refactor:`, `feat:`, `test:`, `build:` prefixes, imperative mood.
- Localized strings come from `TaskManager.Resources.Languages.Strings` (e.g., `Strings.OpsCompletedWithFailuresFormat`, `Strings.SelectProcess`, `Strings.Confirm`, `Strings.AskingForConfirmation`, `Strings.Error`, `Strings.Select`, `Strings.ExportFailedFormat`, `Strings.ExportFailedAccessDenied`, `Strings.ExportFailedInvalidPath`, `Strings.ExportFailedIo`). Reuse verbatim; do not invent keys.
- Message boxes stay in `IMessageService` (`TaskManager.Abstractions`); `IWindowService` is strictly window orchestration.

---

### Task 1: Composition-root hygiene fixes

Fixes the latent double-singleton bug (F5) and removes dead code flagged in the audit. No behavior change.

**Files:**
- Modify: `src/TaskManager/App.xaml.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (dead code only)

- [ ] **Step 1: Fix the double `SettingsService` registration**

In `ConfigureServices` (around lines 93–95), replace:

```csharp
services.AddSingleton<ISettingsStore, JsonSettingsStore>();
services.AddSingleton<ISettingsService, SettingsService>();
```

with a single forwarding registration (one instance serves both identities):

```csharp
services.AddSingleton<ISettingsStore, JsonSettingsStore>();
services.AddSingleton<SettingsService>();
services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
```

Leave the later `services.AddSingleton<SettingsService>();` line (line ~102 area) **removed** — search the method for any second `SettingsService` registration and ensure exactly one `AddSingleton<SettingsService>()` exists.

- [ ] **Step 2: Remove duplicate using**

In `App.xaml.cs`, `using TaskManager.Services;` appears twice (lines 8 and 11). Delete one.

- [ ] **Step 3: Remove dead code in MainWindowViewModel**

Delete the commented-out placeholder comment lines `// icon paths` / `// ...` (lines 35–36 region). Do not change anything else.

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Both green required.

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager/App.xaml.cs src/TaskManager/ViewModels/MainWindowViewModel.cs
git commit -m "refactor: forward settings registration through single instance; prune dead code"
```

---

### Task 2: Move `IDispatcherService` out of Domain into the app

Domain must not reference presentation concepts (spec §1 boundary rule).

**Files:**
- Create: `src/TaskManager/Abstractions/IDispatcherService.cs`
- Delete: `src/TaskManager.Domain/Abstractions/IDispatcherService.cs`
- Modify: `src/TaskManager/Services/WpfDispatcherService.cs`
- Modify: `src/TaskManager.Domain/Services/ProcessManager.cs` (using directive only)
- Modify: `tests/TaskManager.IntegrationTests/ProcessManagement/ProcessManagerTests.cs` (using directives only)

**Interfaces:**
- Produces: `TaskManager.Abstractions.IDispatcherService` with member `void Invoke(Action action);` (identical shape to the old Domain interface). All later tasks use this namespace.

- [ ] **Step 1: Create the app-level interface**

Create `src/TaskManager/Abstractions/IDispatcherService.cs`:

```csharp
namespace TaskManager.Abstractions
{
    /// <summary>
    /// Abstraction over <see cref="System.Windows.Application.Current.Dispatcher"/>.
    /// Presentation concern; intentionally lives in the app, not Domain.
    /// </summary>
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
```

- [ ] **Step 2: Repoint implementations and consumers**

- `src/TaskManager/Services/WpfDispatcherService.cs`: change `using TaskManager.Domain.Abstractions;` → `using TaskManager.Abstractions;`. Class body unchanged.
- `src/TaskManager.Domain/Services/ProcessManager.cs`: add `using TaskManager.Abstractions;` alongside existing usings (it consumes `IDispatcherService` until Task 5 deletes the class).
- `tests/TaskManager.IntegrationTests/ProcessManagement/ProcessManagerTests.cs`: add `using TaskManager.Abstractions;` (the file declares `IDispatcherService _inlineDispatcher`).
- Delete `src/TaskManager.Domain/Abstractions/IDispatcherService.cs` with `git rm`.

- [ ] **Step 3: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Grep confirms no `TaskManager.Domain.Abstractions.IDispatcherService` references remain:

```bash
rg -n "Domain.Abstractions" --glob "*.cs" | rg "IDispatcherService"
# expect: no output
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: move IDispatcherService from Domain into app abstractions"
```

---

### Task 3: Introduce `ProcessListCatalog` in the app

New presentation component owning view state, pipeline, and polling. Registered in DI but **not yet consumed by VMs** (they switch in Task 4; `ProcessManager` is deleted in Task 5).

**Files:**
- Create: `src/TaskManager/Abstractions/IProcessListCatalog.cs`
- Create: `src/TaskManager/Presentation/ProcessListCatalog.cs`
- Test (create): `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs`

**Interfaces:**
- Consumes: `TaskManager.Abstractions.IDispatcherService` (Task 2), `ISystemProcessEnumerator`, `ProcessEnricher`, `ISettingsService`, `ProcessOperationsService` (all existing).
- Produces: `IProcessListCatalog` (below) — Task 4 rewires VMs against exactly these members.

```csharp
// src/TaskManager/Abstractions/IProcessListCatalog.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using TaskManager.Domain.Models;

namespace TaskManager.Abstractions
{
    /// <summary>
    /// Read side + batch operations facade over the live process list.
    /// Implementations raise <see cref="INotifyPropertyChanged"/> for <see cref="ProcessCount"/>.
    /// </summary>
    public interface IProcessListCatalog : INotifyPropertyChanged
    {
        ReadOnlyObservableCollection<ProcessItem> Items { get; }
        int ProcessCount { get; }

        /// <summary>Initial fill followed by polling start; call exactly once at startup.</summary>
        Task InitializeAsync();

        /// <summary>Polling skips when busy; user-initiated waits and restarts the polling phase.</summary>
        Task PerformRefreshAsync(bool isUserInitiated);

        /// <summary>Materialized deep copy of current rows; safe to hold across refreshes.</summary>
        IReadOnlyList<Process> SnapshotForExport();

        /// <summary>Delegates to ProcessOperationsService; expected OS failures are data, not exceptions.</summary>
        ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids);

        /// <summary>Applies priority via ProcessOperationsService then writes base priority back into stored rows.</summary>
        ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority);
    }
}
```

- [ ] **Step 1: Create the implementation**

Port the pipeline from `src/TaskManager.Domain/Services/ProcessManager.cs` verbatim where marked *ported*; replace the timer plumbing with a `PeriodicTimer` loop.

Create `src/TaskManager/Presentation/ProcessListCatalog.cs`:

```csharp
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
        private readonly ProcessOperationsService _processOps;
        private readonly ILogger<ProcessListCatalog> _logger;

        public ProcessListCatalog(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
            ProcessEnricher enricher, ISettingsService settings, ProcessOperationsService processOps,
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
                return _index.Values.Select(i => CopyOf(i.Process)).ToList();
            }
        }

        public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids)
            => _processOps.TerminateProcesses(pids);

        public ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority)
        {
            var summary = _processOps.SetPriority(pids, priority);

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

        // ---- refresh pipeline (ported from ProcessManager) ----

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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Register in DI**

In `App.xaml.cs` `ConfigureServices`, next to the other service registrations (keep `ProcessManager`/`TimerManager` registrations for now — they die in Task 5):

```csharp
services.AddSingleton<ProcessOperationsService>();
services.AddSingleton<ProcessListCatalog>();
services.AddSingleton<IProcessListCatalog>(sp => sp.GetRequiredService<ProcessListCatalog>());
```

Add `using TaskManager.Presentation;` and `using TaskManager.Abstractions;` (the latter likely already present via `IDispatcherService` registration).

- [ ] **Step 3: Create hermetic test suite**

Create `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs`. These are the pipeline/reentrancy/export contracts from the old integration suite, made hermetic (scripted enumerator, inline dispatcher, substituted ops service):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Collections.ObjectModel;
using System.ComponentModel;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;
using TaskManager.Presentation;

namespace TaskManager.UnitTests.Presentation
{
    /// <summary>
    /// Behavior contract of ProcessListCatalog: snapshot-driven refresh batches (add/update/remove,
    /// in-place instance reuse), reentrancy guards, materialized export snapshots, ops delegation
    /// with priority writeback. Fully hermetic: scripted enumerator + inline dispatcher.
    /// </summary>
    public class ProcessListCatalogTests
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly ProcessOperationsService _ops =
            Substitute.For<ProcessOperationsService>(NullLogger<ProcessOperationsService>.Instance);
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IDispatcherService _dispatcher = Substitute.For<IDispatcherService>();
        private readonly ProcessListCatalog _catalog;

        public ProcessListCatalogTests()
        {
            _settings.Current.Returns(new AppSettings
            {
                Language = "English",
                ProcessesRefreshFrequency = RefreshFrequencyType.Low, // 10s; loop never actually ticks in tests
                DateTimeFormat = AppSettings.Defaults.DateTimeFormat
            });

            _dispatcher.When(d => d.Invoke(Arg.Any<Action>()))
                .Do(ci => ((Action)ci[0])());

            _catalog = new ProcessListCatalog(
                _dispatcher,
                _enumerator,
                new CountingEnricher(),
                _settings,
                _ops,
                NullLogger<ProcessListCatalog>.Instance);
        }

        // ---- refresh pipeline ----

        [Fact]
        public async Task FirstRefresh_AddsEverythingFromSnapshot()
        {
            _enumerator.Queue(Snap(1, "alpha"), Snap(2, "beta"));

            await _catalog.LoadForTestAsync();

            _catalog.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 1, 2 });
            _catalog.ProcessCount.ShouldBe(2);
        }

        [Fact]
        public async Task SecondRefresh_UpdatesExistingInstance_InPlace_AndRaisesFieldNotification()
        {
            _enumerator.Queue(Snap(1, "alpha", threadCount: 3));
            await _catalog.LoadForTestAsync();
            var original = _catalog.Items.Single();
            var notified = new List<string>();
            original.Process.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            _enumerator.Queue(Snap(1, "alpha", threadCount: 7));
            await _catalog.SafePollingRefreshAsync();

            _catalog.Items.ShouldHaveSingleItem().ShouldBe(original); // same instance -> selection survives
            original.Process.ThreadCount.ShouldBe(7);
            notified.ShouldContain(nameof(Process.ThreadCount));
        }

        [Fact]
        public async Task SecondRefresh_RemovesVanished_AndAddsNew()
        {
            _enumerator.Queue(Snap(1), Snap(2));
            await _catalog.LoadForTestAsync();

            _enumerator.Queue(Snap(2), Snap(3));
            await _catalog.SafePollingRefreshAsync();

            _catalog.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 2, 3 });
        }

        [Fact]
        public async Task RemovedPid_ReEnrichedOnReuse_EvictsCacheEntry()
        {
            var enumerator = new ScriptedEnumerator();
            var enricher = new CountingEnricher();
            var catalog = new ProcessListCatalog(
                _dispatcher, enumerator, enricher, _settings, _ops,
                NullLogger<ProcessListCatalog>.Instance);

            enumerator.Queue(Snap(7));
            await catalog.LoadForTestAsync();

            enumerator.Queue(); // PID 7 exits
            await catalog.SafePollingRefreshAsync();

            enumerator.Queue(Snap(7)); // PID reused by another image
            await catalog.SafePollingRefreshAsync();

            enricher.Calls.ShouldBe(2); // cache entry evicted, not replayed
        }

        // ---- reentrancy guard ----

        [Fact]
        public async Task OverlappingPollingRefresh_SecondCallSkips_FirstCompletes()
        {
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            _enumerator.Queue(Array.Empty<ProcessSnapshot>);

            var first = _catalog.SafePollingRefreshAsync();
            var second = _catalog.SafePollingRefreshAsync(); // must not queue behind, must skip

            releaseFirst.SetResult();
            await first;
            second.IsCompleted.ShouldBeTrue();
            _enumerator.CallCount.ShouldBe(1); // skipped tick never captured
        }

        [Fact]
        public async Task ManualRefresh_WaitsForInFlight_ToFinish()
        {
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            _enumerator.Queue(Snap(1));

            var first = _catalog.SafePollingRefreshAsync();
            var manual = _catalog.PerformRefreshAsync(isUserInitiated: true);

            manual.IsCompleted.ShouldBeFalse();
            releaseFirst.SetResult();
            await manual;

            _enumerator.CallCount.ShouldBe(2);
            _catalog.ProcessCount.ShouldBe(1);
        }

        // ---- failure containment ----

        [Fact]
        public async Task EnumeratorFailure_IsSwallowedByPollingWrapper()
        {
            _enumerator.ThrowNext(new InvalidOperationException("snapshot boom"));

            await _catalog.SafePollingRefreshAsync();

            _catalog.Items.ShouldBeEmpty();
        }

        [Fact]
        public async Task DispatcherFailure_IsSwallowedByPollingWrapper()
        {
            var throwingDispatcher = Substitute.For<IDispatcherService>();
            throwingDispatcher.When(d => d.Invoke(Arg.Any<Action>()))
                .Do(_ => throw new InvalidOperationException("dispatcher died"));
            var catalog = new ProcessListCatalog(
                throwingDispatcher, ScriptedEnumerator.Of(SelfSnap()), new CountingEnricher(),
                _settings, _ops, NullLogger<ProcessListCatalog>.Instance);

            await catalog.SafePollingRefreshAsync(); // must not throw
        }

        // ---- export snapshot ----

        [Fact]
        public async Task ExportSnapshot_IsImmuneToLaterStoreChanges()
        {
            _enumerator.Queue(Snap(1, "one"), Snap(2, "two"));
            await _catalog.LoadForTestAsync();

            var snapshot = _catalog.SnapshotForExport();

            _enumerator.Queue();
            await _catalog.SafePollingRefreshAsync(); // everything exits

            _catalog.Items.ShouldBeEmpty();
            snapshot.Count.ShouldBe(2);
            snapshot.Single(p => p.Pid == 1).Name.ShouldBe("one");
        }

        // ---- settings-driven polling configuration ----

        [Fact]
        public void SettingsChanged_UpdatesIntervalMapping()
        {
            _catalog.CurrentIntervalSeconds.ShouldBe(10); // Low => 10s from Current snapshot

            var high = AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.High };
            _settings.Current.Returns(high);                        // service swaps snapshot first
            _settings.Changed += Raise.Event<Action<AppSettings>>(high); // then notifies consumers

            _catalog.CurrentIntervalSeconds.ShouldBe(5); // High => 5s
        }

        // ---- batch operation delegation + writeback ----

        [Fact]
        public void TerminateProcesses_DelegatesToOpsService()
        {
            _ops.TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>())
                .Returns(ProcessOpSummary.Empty);

            _catalog.TerminateProcesses([42]);

            _ops.Received(1).TerminateProcesses(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 42));
        }

        [Fact]
        public void SetPriority_SuccessfulPids_AreWrittenBackIntoStoredRows()
        {
            _enumerator.Queue(Snap(9));
            _catalog.LoadForTestAsync().GetAwaiter().GetResult();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary { SucceededPids = [9], Failures = [] });

            var summary = _catalog.SetPriority([9], ProcessPriorityClass.AboveNormal);

            summary.HasFailures.ShouldBeFalse();
            _catalog.Items.Single().Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public void SetPriority_FailedPids_AreNotWrittenBack()
        {
            _enumerator.Queue(Snap(9));
            _catalog.LoadForTestAsync().GetAwaiter().GetResult();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary
                {
                    SucceededPids = [],
                    Failures = [new ProcessOpFailure(9, ProcessOpFailureReason.AccessDenied)]
                });

            _catalog.SetPriority([9], ProcessPriorityClass.BelowNormal);

            _catalog.Items.Single().Process.Priority.ShouldNotBe(6); // untouched
        }

        // ---- helpers ----

        private static ProcessSnapshot SelfSnap() =>
            new(Environment.ProcessId, "self", ThreadCount: 1, Ppid: null, BasePriority: 8);

        private static ProcessSnapshot Snap(int pid, string name = "n", int threadCount = 1) =>
            new(pid, name, ThreadCount: threadCount, Ppid: 4, BasePriority: 8);

        private sealed class ScriptedEnumerator : ISystemProcessEnumerator
        {
            private readonly Queue<Func<IReadOnlyList<ProcessSnapshot>>> _scripts = new();
            public int CallCount { get; private set; }
            private Exception? _throwOnce;

            public static ScriptedEnumerator Of(params ProcessSnapshot[] items)
            {
                var e = new ScriptedEnumerator();
                e.Queue(items);
                return e;
            }

            public void Queue(params ProcessSnapshot[] items) => Queue(() => items);

            public void Queue(Func<IReadOnlyList<ProcessSnapshot>> script) => _scripts.Enqueue(script);

            public void ThrowNext(Exception ex) => _throwOnce = ex;

            public IReadOnlyList<ProcessSnapshot> Capture()
            {
                CallCount++;
                if (_throwOnce is not null)
                {
                    var ex = _throwOnce;
                    _throwOnce = null;
                    throw ex;
                }

                return _scripts.Dequeue()();
            }
        }

        private sealed class CountingEnricher : ProcessEnricher
        {
            public int Calls { get; private set; }

            public CountingEnricher() : base(NullLogger<ProcessEnricher>.Instance)
            {
            }

            public override bool TryEnrich(int pid, out ProcessEnrichment enrichment)
            {
                Calls++;
                enrichment = new ProcessEnrichment($@"C:\fake-{pid}-{Calls}.exe", ArchitectureType._64BIT);
                return true;
            }
        }
    }
}
```

The tests use two seams that must exist on `ProcessListCatalog` (add them if you skipped them in Step 1): `internal int CurrentIntervalSeconds` (already in the listing) and a test-only alias for the non-polling initial fill:

```csharp
// Add to ProcessListCatalog (test convenience; avoids starting the polling loop in tests):
internal Task LoadForTestAsync() => SafePollingRefreshAsync();
```

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager/Abstractions/IProcessListCatalog.cs src/TaskManager/Presentation/ProcessListCatalog.cs src/TaskManager/App.xaml.cs tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs
git commit -m "feat: introduce ProcessListCatalog presentation component with PeriodicTimer polling"
```

---

### Task 4: Decouple ViewModels — `IWindowService`, `IFolderPicker`, exporter delegate

All four VMs lose `IServiceProvider`, windows, and factories. `ViewModelBase`, `Preconditions`, `FolderSelector`, and all three factory classes die.

**Files:**
- Create: `src/TaskManager/Abstractions/IWindowService.cs`
- Create: `src/TaskManager/Abstractions/IFolderPicker.cs`
- Create: `src/TaskManager/Services/WindowService.cs`
- Create: `src/TaskManager/Services/FolderPicker.cs`
- Create: `src/TaskManager/ViewModels/IRequestCloseObservable.cs`
- Create: `src/TaskManager/ViewModels/OperationSummaryReporter.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (rewrite)
- Modify: `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs` (rewrite)
- Modify: `src/TaskManager/ViewModels/DataExportWindowViewModel.cs` (rewrite)
- Delete: `src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs` (+ folder), `src/TaskManager/ViewModels/Preconditions.cs`, `src/TaskManager/Services/FolderSelector.cs`, `src/TaskManager/Services/Factories/DataExportViewModelFactory.cs`, `src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs`, `src/TaskManager/Services/Factories/DataExporterFactory.cs` (+ Factories folder)
- Modify: `src/TaskManager/App.xaml.cs` (registrations + `LaunchGUI`)
- Test (create): `tests/TaskManager.UnitTests/ViewModels/MainWindowViewModelTests.cs`
- Test (create): `tests/TaskManager.UnitTests/ViewModels/SetPriorityWindowViewModelTests.cs`
- Test (create): `tests/TaskManager.UnitTests/ViewModels/OperationSummaryReporterTests.cs`
- Test (rewrite): `tests/TaskManager.UnitTests/ViewModels/DataExportWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `IProcessListCatalog` (Task 3), `IDispatcherService` (Task 2, indirectly), `IMessageService`, `IErrorHandler` + `Guard`/`GuardAsync` extensions, `Strings` resources, `BaseDataExporter.Export<T>(string dirPath, IEnumerable<T> records)` returning `ExportResult`.
- Produces (exact signatures later tasks/tests rely on):

```csharp
// src/TaskManager/Abstractions/IWindowService.cs
using TaskManager.Domain.Models;

namespace TaskManager.Abstractions
{
    /// <summary>Window orchestration owned by the composition root. Message boxes stay in IMessageService.</summary>
    public interface IWindowService
    {
        /// <summary>Opens the settings dialog modally; returns after it closes.</summary>
        void ShowSettings();

        /// <summary>Opens the data-export dialog seeded with a materialized process snapshot.</summary>
        void ShowExport(IReadOnlyList<Process> processes);

        /// <summary>Opens the priority dialog for the given PIDs. True = user confirmed and the operation applied.</summary>
        bool ShowSetPriority(IReadOnlyCollection<int> pids);
    }
}
```

```csharp
// src/TaskManager/Abstractions/IFolderPicker.cs
namespace TaskManager.Abstractions
{
    public interface IFolderPicker
    {
        /// <returns>Absolute folder path, or null when the user cancelled.</returns>
        string? PickFolder();
    }
}
```

```csharp
// src/TaskManager/ViewModels/IRequestCloseObservable.cs
namespace TaskManager.ViewModels
{
    /// <summary>Implemented by dialog VMs; the hosting window closes when this fires.</summary>
    internal interface IRequestCloseObservable
    {
        event EventHandler? RequestClose;
    }
}
```

- [ ] **Step 1: Create `WindowService` and `FolderPicker`**

```csharp
// src/TaskManager/Services/WindowService.cs
using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Services
{
    /// <summary>
    /// Composition-root window orchestration: builds windows + their ViewModels, owns dialog
    /// flow and close relaying. This is the ONLY place that constructs windows.
    /// </summary>
    internal sealed class WindowService : IWindowService
    {
        private readonly ISettingsService _settings;
        private readonly IErrorHandler _errorHandler;
        private readonly IMessageService _messages;
        private readonly IProcessListCatalog _catalog;
        private readonly Func<DataType, BaseDataExporter> _exporterFactory;
        private readonly IFolderPicker _folderPicker;

        public WindowService(ISettingsService settings, IErrorHandler errorHandler, IMessageService messages,
            IProcessListCatalog catalog, Func<DataType, BaseDataExporter> exporterFactory, IFolderPicker folderPicker)
        {
            _settings = settings;
            _errorHandler = errorHandler;
            _messages = messages;
            _catalog = catalog;
            _exporterFactory = exporterFactory;
            _folderPicker = folderPicker;
        }

        public void ShowSettings()
        {
            var window = new SettingsWindow
            {
                DataContext = new SettingsWindowViewModel(_settings, _errorHandler)
            };
            window.ShowDialog();
        }

        public void ShowExport(IReadOnlyList<Process> processes)
        {
            var vm = new DataExportWindowViewModel(_messages, _errorHandler, _exporterFactory, _folderPicker, processes);
            ShowDialogWithCloseRelay(new DataExportWindow(), vm);
        }

        public bool ShowSetPriority(IReadOnlyCollection<int> pids)
        {
            var vm = new SetPriorityWindowViewModel(_messages, _catalog, pids, _errorHandler);
            ShowDialogWithCloseRelay(new SetPriorityWindow(), vm);
            return vm.Confirmed;
        }

        private static void ShowDialogWithCloseRelay(Window window, ObservableObject vm)
        {
            window.DataContext = vm;
            if (vm is IRequestCloseObservable closable)
            {
                closable.RequestClose += (_, _) => window.Close();
            }

            window.ShowDialog();
        }
    }
}
```

```csharp
// src/TaskManager/Services/FolderPicker.cs
using Microsoft.Win32;
using TaskManager.Abstractions;

namespace TaskManager.Services
{
    internal sealed class FolderPicker : IFolderPicker
    {
        public string? PickFolder()
        {
            var dialog = new OpenFolderDialog();
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
```

- [ ] **Step 2: Rewrite `MainWindowViewModel`**

Replace the entire file content:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for MainWindow. Headless-testable: depends only on interfaces; all window
    /// orchestration goes through IWindowService; startup happens via InitializeCommand
    /// invoked once by the composition root.
    /// </summary>
    internal class MainWindowViewModel : ObservableObject
    {
        private readonly IMessageService _messageService;
        private readonly IProcessListCatalog _catalog;
        private readonly IErrorHandler _errorHandler;
        private readonly ISettingsService _settings;
        private readonly IWindowService _windows;

        public MainWindowViewModel(IMessageService messageService, IProcessListCatalog catalog,
            IErrorHandler errorHandler, ISettingsService settings, IWindowService windows)
        {
            _messageService = messageService;
            _catalog = catalog;
            _errorHandler = errorHandler;
            _settings = settings;
            _windows = windows;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new RelayCommand(TerminateProcesses);
            SetPriorityCommand = new RelayCommand(SetPriority);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.PerformRefreshAsync(isUserInitiated: true), "refreshing process list"));
            InitializeCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.InitializeAsync(), "loading initial process list"));

            // count forwarder: ProcessCount is owned by the catalog
            _catalog.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IProcessListCatalog.ProcessCount))
                {
                    OnPropertyChanged(nameof(ProcessCount));
                }
            };
        }

        #region Bindings
        public int ProcessCount => _catalog.ProcessCount;
        public IList<DataType> DataTypes => Enum.GetValues<DataType>();
        public ReadOnlyObservableCollection<ProcessItem> Processes => _catalog.Items;

        public ImageSource? MonitoringButtonIcon { get; set => SetProperty(ref field, value); }
        public int SelectedTabIndex { get; set => SetProperty(ref field, value); }
        #endregion

        #region Commands
        public ICommand ExportCommand { get; }
        public ICommand TerminateCommand { get; }
        public ICommand SetPriorityCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand InitializeCommand { get; }
        #endregion

        private void OpenSettings()
        {
            // resx/x:Static localization is baked at compile time, so a language
            // switch still requires a restart; the decision lives here (composition flow).
            var languageBefore = _settings.Current.Language;

            _windows.ShowSettings();

            if (_settings.Current.Language != languageBefore)
            {
                App.Restart();
            }
        }

        private void Export() => _windows.ShowExport(_catalog.SnapshotForExport());

        private IEnumerable<ProcessItem> GetSelectedProcesses() => Processes.Where(p => p.IsSelected);

        private void TerminateProcesses()
        {
            if (!EnsureSelection())
            {
                return;
            }

            if (_messageService.ShowMessage(Strings.AskingForConfirmation, Strings.Confirm,
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
            {
                return;
            }

            _errorHandler.Guard(() =>
            {
                var summary = _catalog.TerminateProcesses(
                    GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
                ReportPartialFailures(summary);
            });
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            _messageService.ShowMessage(OperationSummaryReporter.FormatPartialFailures(summary),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SetPriority()
        {
            if (!EnsureSelection())
            {
                return;
            }

            _windows.ShowSetPriority(
                GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
        }

        private bool EnsureSelection()
        {
            if (Processes.Any(x => x.IsSelected))
            {
                return true;
            }

            _messageService.ShowMessage(Strings.SelectProcess, Strings.Error,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
```

- [ ] **Step 3: Rewrite `SetPriorityWindowViewModel`**

Replace the entire file content:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.UI.Localization;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for SetPriorityWindow. Applies the chosen priority through the catalog,
    /// reports partial failures as data, then raises RequestClose (host window closes).
    /// </summary>
    internal class SetPriorityWindowViewModel : ObservableObject, IRequestCloseObservable
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();

        public ProcessPriorityClass? Priority { get; set => SetProperty(ref field, value); }

        public ICommand OnConfirmCommand { get; }

        public event EventHandler? RequestClose;

        public bool Confirmed { get; private set; }

        private readonly IMessageService _messageService;
        private readonly IProcessListCatalog _catalog;
        private readonly IErrorHandler _errorHandler;
        private readonly IReadOnlyCollection<int> _processIds;

        public SetPriorityWindowViewModel(IMessageService messageService, IProcessListCatalog catalog,
            IReadOnlyCollection<int> processes, IErrorHandler errorHandler)
        {
            _messageService = messageService;
            _catalog = catalog;
            _processIds = processes;
            _errorHandler = errorHandler;
            OnConfirmCommand = new RelayCommand(OnConfirm);
        }

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Priority == null)
                {
                    _messageService.ShowMessage(Strings.Select, Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var summary = _catalog.SetPriority(_processIds, (ProcessPriorityClass)Priority);
                ReportPartialFailures(summary);

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "applying the selected priority");
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            _messageService.ShowMessage(OperationSummaryReporter.FormatPartialFailures(summary),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
```

- [ ] **Step 4: Rewrite `DataExportWindowViewModel`**

Replace the entire file content. Notes: the old `ISettingsService` dependency was stored but never used — dropped. `TryExport` loses its single-case `switch`; exporters come from the injected factory delegate. Cancelled folder picking clears `DirPath` (previous UX preserved).

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for DataExportWindow. Exports a fixed, materialized process snapshot via
    /// the composition-root-supplied exporter factory; modeled failures are reported as data.
    /// </summary>
    internal class DataExportWindowViewModel : ObservableObject, IRequestCloseObservable
    {
        public ExportationType? Exportation { get; set => SetProperty(ref field, value); }

        public DataType? DataType { get; set => SetProperty(ref field, value); }

        public string DirPath { get; set => SetProperty(ref field, value); } = string.Empty;

        public IList<ExportationType> Exportations { get; } = Enum.GetValues<ExportationType>();
        public IList<DataType> Extensions { get; } = Enum.GetValues<DataType>();

        public ICommand SelectFolderCommand { get; }
        public ICommand OnConfirmClick { get; }

        public event EventHandler? RequestClose;

        public bool Confirmed { get; private set; }

        private readonly IReadOnlyList<Process> _processes;
        private readonly Func<DataType, BaseDataExporter> _exporterFactory;
        private readonly IFolderPicker _folderPicker;
        private readonly IMessageService _messageService;
        private readonly IErrorHandler _errorHandler;

        public DataExportWindowViewModel(IMessageService messageService, IErrorHandler errorHandler,
            Func<DataType, BaseDataExporter> exporterFactory, IFolderPicker folderPicker,
            IReadOnlyList<Process> processes)
        {
            _messageService = messageService;
            _errorHandler = errorHandler;
            _exporterFactory = exporterFactory;
            _folderPicker = folderPicker;
            _processes = processes.ToArray(); // hold a materialized copy; caller may mutate afterwards

            SelectFolderCommand = new RelayCommand(SelectFolder);
            OnConfirmClick = new RelayCommand(OnConfirm);
        }

        private void SelectFolder() => DirPath = _folderPicker.PickFolder() ?? string.Empty;

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
                {
                    _messageService.ShowMessage("You need to select options", Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!TryExport(dataType))
                {
                    return; // failure already reported; keep the window open for a corrected attempt
                }

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "exporting process data");
        }

        internal bool TryExport(DataType dataType)
        {
            return _errorHandler.Guard(() =>
            {
                var result = _exporterFactory(dataType).Export(DirPath, _processes);
                if (result.IsSuccess)
                {
                    return true;
                }

                _messageService.ShowMessage(
                    string.Format(Strings.ExportFailedFormat, DirPath) + " " + DescribeFailure(result.FailureReason!.Value),
                    Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }, "exporting process data");
        }

        private static string DescribeFailure(ExportFailureReason reason) => reason switch
        {
            ExportFailureReason.AccessDenied => Strings.ExportFailedAccessDenied,
            ExportFailureReason.InvalidPath => Strings.ExportFailedInvalidPath,
            _ => Strings.ExportFailedIo
        };
    }
}
```

- [ ] **Step 5: Create `OperationSummaryReporter`**

```csharp
// src/TaskManager/ViewModels/OperationSummaryReporter.cs
using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;

namespace TaskManager.ViewModels
{
    /// <summary>Single formatting point for partial-failure batch reports.</summary>
    internal static class OperationSummaryReporter
    {
        public static string FormatPartialFailures(ProcessOpSummary summary) =>
            string.Format(Strings.OpsCompletedWithFailuresFormat,
                summary.SucceededPids.Count,
                summary.SucceededPids.Count + summary.Failures.Count);
    }
}
```

- [ ] **Step 6: Update composition root**

In `App.xaml.cs`:

a) Replace the factory/service registrations block (the lines registering `FolderSelector`, `DataExporterFactory`, `DataExportViewModelFactory`, `SetPriorityVVmFactory`) with:

```csharp
services.AddSingleton<IFolderPicker, FolderPicker>();
services.AddSingleton<Func<DataType, BaseDataExporter>>(sp => dataType => dataType switch
{
    DataType.Csv => new CsvExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
    DataType.Txt => new TxtExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
    DataType.Xlsx => new ExcelExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
    DataType.Json => new JsonExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
    DataType.Xml => new XmlExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
    _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
});
services.AddSingleton<IWindowService, WindowService>();
```

b) Replace the ViewModel/Views registrations block with (only `MainWindow` stays DI-constructed; dialogs are built by `WindowService`):

```csharp
// Register ViewModels
services.AddSingleton<MainWindowViewModel>();

// Register Views
services.AddSingleton<MainWindow>(sp =>
{
    return new MainWindow
    {
        DataContext = sp.GetRequiredService<MainWindowViewModel>()
    };
});
```

(Remove `SettingsWindowViewModel` registration and the `SettingsWindow`/`DataExportWindow` registrations.)

c) Replace `LaunchGUI` with the single startup point (initial load + polling start; the command wraps everything in `GuardAsync`, so discarding the task cannot crash the app):

```csharp
private void LaunchGUI()
{
    MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
    mainWindow.Show();

    // Single startup point: initial load + polling start. Failures route through
    // IErrorHandler.GuardAsync inside InitializeCommand and are logged, not fatal.
    _ = _serviceProvider.GetRequiredService<MainWindowViewModel>().InitializeCommand.ExecuteAsync(null);
}
```

d) Required usings (add/remove as needed): `TaskManager.Abstractions`, `TaskManager.Presentation`, `TaskManager.Domain.Services.DataExport`, `TaskManager.Domain.Primitives`, `Microsoft.Extensions.Logging`. Keep `using TaskManager.Domain.Services;` while the `ProcessManager`/`TimerManager`/`ProcessOperationsService` registration lines still exist (they are removed in Task 5).

e) Delete files with `git rm`:

```
src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs
src/TaskManager/ViewModels/Preconditions.cs
src/TaskManager/Services/FolderSelector.cs
src/TaskManager/Services/Factories/DataExportViewModelFactory.cs
src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs
src/TaskManager/Services/Factories/DataExporterFactory.cs
```

- [ ] **Step 7: Add headless VM tests**

Create `tests/TaskManager.UnitTests/ViewModels/OperationSummaryReporterTests.cs`:

```csharp
using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    public class OperationSummaryReporterTests
    {
        [Fact]
        public void FormatPartialFailures_ReportsSucceededCountAndTotal()
        {
            var summary = new ProcessOpSummary
            {
                SucceededPids = [1, 2],
                Failures = [new ProcessOpFailure(3, ProcessOpFailureReason.AccessDenied)]
            };

            var formatted = OperationSummaryReporter.FormatPartialFailures(summary);

            formatted.ShouldBe(string.Format(Strings.OpsCompletedWithFailuresFormat, 2, 3));
        }

        [Fact]
        public void FormatPartialFailures_AllSucceeded_FormattedAnyway()
        {
            var summary = new ProcessOpSummary { SucceededPids = [1], Failures = [] };

            OperationSummaryReporter.FormatPartialFailures(summary)
                .ShouldBe(string.Format(Strings.OpsCompletedWithFailuresFormat, 1, 1));
        }
    }
}
```

Create `tests/TaskManager.UnitTests/ViewModels/MainWindowViewModelTests.cs`:

```csharp
using NSubstitute;
using System.Collections.ObjectModel;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    /// <summary>
    /// Headless main-window flows: selection precondition, terminate confirmation gate,
    /// partial-failure reporting, delegation of dialogs/snapshot to services.
    /// </summary>
    public class MainWindowViewModelTests
    {
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IWindowService _windows = Substitute.For<IWindowService>();

        public MainWindowViewModelTests()
        {
            _catalog.Items.Returns(new ReadOnlyObservableCollection<ProcessItem>(new ObservableCollection<ProcessItem>()));
        }

        private MainWindowViewModel CreateViewModel() =>
            new(_messages, _catalog, _errorHandler, _settings, _windows);

        private static ProcessItem Row(int pid, bool selected = false) =>
            new() { Process = new Process { Name = $"p{pid}", Pid = pid, Path = string.Empty }, IsSelected = selected };

        private void SeedRows(params ProcessItem[] rows) =>
            _catalog.Items.Returns(new ReadOnlyObservableCollection<ProcessItem>(new ObservableCollection<ProcessItem>(rows)));

        [Fact]
        public void Constructor_DoesNotTouchCatalogSideEffects()
        {
            CreateViewModel();

            _catalog.DidNotReceiveWithAnyArgs().InitializeAsync();
        }

        [Fact]
        public async Task InitializeCommand_LoadsCatalogExactlyOnce()
        {
            await CreateViewModel().InitializeCommand.ExecuteAsync(null);

            await _catalog.Received(1).InitializeAsync();
        }

        [Fact]
        public void Terminate_NoSelection_ShowsAlert_AndNeverTouchesCatalogOrConfirm()
        {
            var vm = CreateViewModel();

            vm.TerminateCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _catalog.DidNotReceive().TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public void Terminate_UserCancelsConfirmation_OperationSkipped()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.Cancel);
            var vm = CreateViewModel();

            vm.TerminateCommand.Execute(null);

            _catalog.DidNotReceive().TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public void Terminate_Confirmed_CleanSummary_NoExtraMessages()
        {
            SeedRows(Row(1, selected: true), Row(2, selected: false));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>()).Returns(ProcessOpSummary.Empty);
            var vm = CreateViewModel();

            vm.TerminateCommand.Execute(null);

            _catalog.Received(1).TerminateProcesses(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 1)); // unselected row excluded
            _messages.ReceivedCalls().Count().ShouldBe(1); // confirmation prompt only
        }

        [Fact]
        public void Terminate_PartialFailures_ReportsFormattedSummary()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>()).Returns(new ProcessOpSummary
            {
                SucceededPids = [],
                Failures = [new ProcessOpFailure(1, ProcessOpFailureReason.AccessDenied)]
            });
            var vm = CreateViewModel();

            vm.TerminateCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                OperationSummaryReporter.FormatPartialFailures(It.IsAny<ProcessOpSummary>()),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [Fact]
        public void Export_PassesMaterializedSnapshot_ToWindowService()
        {
            var snapshot = new List<Process> { new() { Name = "p1", Pid = 1, Path = string.Empty } };
            _catalog.SnapshotForExport().Returns(snapshot);
            var vm = CreateViewModel();

            vm.ExportCommand.Execute(null);

            _windows.Received(1).ShowExport(Arg.Is<IReadOnlyList<Process>>(p => p.Single().Pid == 1));
        }

        [Fact]
        public void SetPriority_NoSelection_Alerts_AndDoesNotOpenDialog()
        {
            var vm = CreateViewModel();

            vm.SetPriorityCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _windows.DidNotReceive().ShowSetPriority(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public void SetPriority_Selected_DelegatesSelectedPidsToWindowService()
        {
            SeedRows(Row(5, selected: true), Row(6, selected: true));
            var vm = CreateViewModel();

            vm.SetPriorityCommand.Execute(null);

            _windows.Received(1).ShowSetPriority(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.OrderBy(x => x).SequenceEqual(new[] { 5, 6 })));
        }
    }
}
```

Create `tests/TaskManager.UnitTests/ViewModels/SetPriorityWindowViewModelTests.cs`:

```csharp
using NSubstitute;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    /// <summary>
    /// Confirm-flow contract: missing selection alerts without closing; success applies via
    /// catalog, marks Confirmed, raises RequestClose; partial failures report formatted data.
    /// </summary>
    public class SetPriorityWindowViewModelTests
    {
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private static readonly IReadOnlyCollection<int> Pids = [7, 8];

        private SetPriorityWindowViewModel CreateViewModel() =>
            new(_messages, _catalog, Pids, _errorHandler);

        [Fact]
        public void Confirm_NoPriorityChosen_ShowsError_DoesNotApplyOrClose()
        {
            var vm = CreateViewModel();
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                Strings.Select, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _catalog.DidNotReceive().SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>());
            closed.ShouldBeFalse();
            vm.Confirmed.ShouldBeFalse();
        }

        [Fact]
        public void Confirm_AppliesViaCatalog_MarksConfirmed_RaisesRequestClose()
        {
            _catalog.SetPriority(Pids, ProcessPriorityClass.AboveNormal).Returns(ProcessOpSummary.Empty);
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.AboveNormal;
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            _catalog.Received(1).SetPriority(Pids, ProcessPriorityClass.AboveNormal);
            vm.Confirmed.ShouldBeTrue();
            closed.ShouldBeTrue();
        }

        [Fact]
        public void Confirm_PartialFailures_ReportsFormattedSummary_BeforeClosing()
        {
            _catalog.SetPriority(Pids, ProcessPriorityClass.High).Returns(new ProcessOpSummary
            {
                SucceededPids = [7],
                Failures = [new ProcessOpFailure(8, ProcessOpFailureReason.ProcessExited)]
            });
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.High;

            vm.OnConfirmCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                OperationSummaryReporter.FormatPartialFailures(It.IsAny<ProcessOpSummary>()),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
            vm.Confirmed.ShouldBeTrue(); // batch survived; dialog still closes
        }

        [Fact]
        public void Confirm_CatalogThrows_IsGuarded_DoesNotClose()
        {
            _catalog.When(c => c.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>()))
                .Do(_ => throw new InvalidOperationException("os exploded"));
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.Normal;
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            closed.ShouldBeFalse();
            vm.Confirmed.ShouldBeFalse();
        }
    }
}
```

- [ ] **Step 8: Rewrite `DataExportWindowViewModelTests`**

Replace the entire file content (no more service provider, no factory subclass; the exporter factory is a substituted delegate; picker behavior covered too):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;
using TaskManager.Domain.Primitives;
using DataTypeEnum = TaskManager.Domain.Primitives.DataType;
using ExportationTypeEnum = TaskManager.Domain.Primitives.ExportationType;

namespace TaskManager.UnitTests.ViewModels
{
    public class DataExportWindowViewModelTests : IDisposable
    {
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly Func<DataTypeEnum, BaseDataExporter> _exporterFactory =
            Substitute.For<Func<DataTypeEnum, BaseDataExporter>>();
        private readonly IFolderPicker _folderPicker = Substitute.For<IFolderPicker>();
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-exportvm-tests-{Guid.NewGuid():N}");
        private readonly DataExportWindowViewModel _viewModel;

        public DataExportWindowViewModelTests()
        {
            Directory.CreateDirectory(_tempDirectory);
            _viewModel = new DataExportWindowViewModel(
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                _exporterFactory,
                _folderPicker,
                []);
            _viewModel.DirPath = _tempDirectory;
        }

        [Fact]
        public void TryExport_Success_WritesFileAndReturnsTrue()
        {
            _exporterFactory(DataTypeEnum.Txt)
                .Returns(new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance));

            var success = _viewModel.TryExport(DataTypeEnum.Txt);

            success.ShouldBeTrue();
            Directory.GetFiles(_tempDirectory, "record-*").ShouldNotBeEmpty();
        }

        [Fact]
        public void TryExport_Failure_ShowsSingleMessageAndReturnsFalse()
        {
            _exporterFactory(DataTypeEnum.Txt)
                .Returns(new ThrowingExporter(NewSettings()));

            var success = _viewModel.TryExport(DataTypeEnum.Txt);

            success.ShouldBeFalse();
            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void TryExport_UnexpectedFactoryCrash_IsGuardedAndReturnsFalse()
        {
            _exporterFactory.When(f => f(DataTypeEnum.Txt))
                .Do(_ => throw new InvalidOperationException("factory exploded"));

            _viewModel.TryExport(DataTypeEnum.Txt).ShouldBeFalse();
        }

        [Fact]
        public void Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations()
        {
            var processes = new List<Process>
            {
                new() { Name = "p1", Pid = 1, Path = string.Empty },
                new() { Name = "p2", Pid = 2, Path = string.Empty },
            };
            _exporterFactory(DataTypeEnum.Txt)
                .Returns(new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance));
            var vm = new DataExportWindowViewModel(
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                _exporterFactory,
                _folderPicker,
                processes);
            vm.DirPath = _tempDirectory;

            processes.Clear(); // caller-side mutation after handoff must not leak into the dialog

            vm.TryExport(DataTypeEnum.Txt).ShouldBeTrue();

            var written = File.ReadAllLines(Directory.GetFiles(_tempDirectory, "record-*").Single());
            written.Count(line => line.Contains("p1")).ShouldBe(1);
            written.Count(line => line.Contains("p2")).ShouldBe(1);
        }

        [Fact]
        public void SelectFolder_PickedResult_AssignedToDirPath()
        {
            var picked = Path.Combine(_tempDirectory, "picked");
            Directory.CreateDirectory(picked);
            _folderPicker.PickFolder().Returns(picked);

            _viewModel.SelectFolderCommand.Execute(null);

            _viewModel.DirPath.ShouldBe(picked);
        }

        [Fact]
        public void SelectFolder_Cancelled_ClearsDirPath_PreservingPreviousUx()
        {
            _folderPicker.PickFolder().Returns((string?)null);

            _viewModel.SelectFolderCommand.Execute(null);

            _viewModel.DirPath.ShouldBeEmpty();
        }

        [Fact]
        public void OnConfirm_MissingOptions_ShowsError_DoesNotClose()
        {
            var closed = false;
            _viewModel.RequestClose += (_, _) => closed = true;

            _viewModel.OnConfirmClick.Execute(null);

            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
            closed.ShouldBeFalse();
            _viewModel.Confirmed.ShouldBeFalse();
        }

        private static ISettingsService NewSettings()
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings
            {
                Language = "English",
                ProcessesRefreshFrequency = RefreshFrequencyType.Low,
                DateTimeFormat = "yyyyMMdd_HHmmss"
            });
            return settings;
        }

        private sealed class ThrowingExporter : TxtExporter
        {
            public ThrowingExporter(ISettingsService settings)
                : base(settings, NullLogger<BaseDataExporter>.Instance)
            {
            }

            protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
                => throw new IOException("target locked");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
```

Note: `TxtExporter` requires `using Microsoft.Extensions.Logging.Abstractions;` and `TaskManager.Domain.Abstractions` is satisfied transitively; keep the usings exactly as listed.

- [ ] **Step 9: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Also grep to confirm the deleted types have no remaining references:

```bash
rg -n "IServiceProvider|GetAssociatedWindow|Preconditions|FolderSelector|ViewModelBase|DataExporterFactory|DataExportViewModelFactory|SetPriorityVVmFactory|GetRequiredService" src/TaskManager --glob "!bin/**"
# expect: matches ONLY inside App.xaml.cs (composition root GetRequiredService calls) — nowhere else
```

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: decouple viewmodels via IWindowService/IFolderCatalog interfaces; delete factories and window lookups"
```

---

### Task 5: Dissolve `ProcessManager` and `TimerManager`; migrate integration tests

VMs consume `IProcessListCatalog` exclusively since Task 4; now the Domain god-service and the timer wrapper die, and the integration suite targets the surviving pieces.

**Files:**
- Delete: `src/TaskManager.Domain/Services/ProcessManager.cs`, `src/TaskManager.Domain/Services/TimerManager.cs`
- Modify: `src/TaskManager/App.xaml.cs` (drop their registrations + stale usings)
- Delete: `tests/TaskManager.IntegrationTests/ProcessManagement/ProcessManagerTests.cs`
- Test (create): `tests/TaskManager.IntegrationTests/ProcessManagement/ProcessOperationsServiceTests.cs`

**Interfaces:**
- Consumes: `ProcessOperationsService` (existing, unchanged): `ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids, Action<int>? onSuccess = null)` and `ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority, Action<int>? onSuccess = null)`.
- Produces: integration contract coverage for those exact signatures.

- [ ] **Step 1: Remove registrations and delete classes**

In `App.xaml.cs` delete these lines (and any usings the compiler flags as unused afterwards, e.g. `TaskManager.Domain.Services` once no Domain service type is referenced):

```csharp
services.AddSingleton<ProcessManager>();
services.AddSingleton<TimerManager>();
```

Then `git rm src/TaskManager.Domain/Services/ProcessManager.cs src/TaskManager.Domain/Services/TimerManager.cs`.

If `ProcessManager.cs` carried the last `using TaskManager.Abstractions;` need inside Domain, verify with grep that no Domain file references `TaskManager.Abstractions` anymore:

```bash
rg -n "TaskManager\.Abstractions" src/TaskManager.Domain
# expect: no output (Domain is presentation-free)
```

- [ ] **Step 2: Create the integration suite for `ProcessOperationsService`**

Create `tests/TaskManager.IntegrationTests/ProcessManagement/ProcessOperationsServiceTests.cs` — carries over every live-OS contract from the old `ProcessManagerTests` that concerns actual process operations (pipeline/reentrancy/export contracts moved to the hermetic catalog suite in Task 3):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;
using WinProcess = System.Diagnostics.Process;
using WinProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace TaskManager.IntegrationTests.ProcessManagement
{
    /// <summary>
    /// Live-OS behavior contract of ProcessOperationsService: real priority changes land,
    /// expected OS rejections (stale/exited PIDs, access denied) are reported as data and
    /// never abort the batch, onSuccess fires only for succeeded PIDs.
    /// </summary>
    public class ProcessOperationsServiceTests : IDisposable
    {
        private readonly ProcessOperationsService _ops =
            new(NullLogger<ProcessOperationsService>.Instance);
        private readonly WinProcess _self = WinProcess.GetCurrentProcess();
        private readonly ProcessPriorityClass _originalPriority;

        public ProcessOperationsServiceTests() => _originalPriority = _self.PriorityClass;

        public void Dispose() => _self.PriorityClass = _originalPriority;

        [Fact]
        public void SetPriority_UpdatesRealProcess_AndFiresOnSuccess()
        {
            int? succeededPid = null;

            var summary = _ops.SetPriority([_self.Id], ProcessPriorityClass.AboveNormal,
                pid => succeededPid = pid);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
            succeededPid.ShouldBe(_self.Id);
            summary.Failures.ShouldBeEmpty();
        }

        [Fact]
        public void SetPriority_StalePid_IsReportedAsExited_AndSurvives()
        {
            int stalePid = GetUnusedPid();

            var summary = _ops.SetPriority([stalePid, _self.Id], ProcessPriorityClass.BelowNormal);

            summary.SucceededPids.ShouldBe([_self.Id]);
            var failure = summary.Failures.ShouldHaveSingleItem();
            failure.Pid.ShouldBe(stalePid);
            failure.Reason.ShouldBe(ProcessOpFailureReason.ProcessExited);
            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.BelowNormal);
        }

        [Fact]
        public void SetPriority_OnSuccess_FiresOnlyForSucceededPids()
        {
            int stalePid = GetUnusedPid();
            var writebacks = new List<int>();

            var summary = _ops.SetPriority([stalePid, _self.Id], ProcessPriorityClass.Idle,
                writebacks.Add);

            writebacks.ShouldBe([_self.Id]);
            summary.Failures.ShouldHaveSingleItem();
        }

        [Fact]
        public void TerminateProcesses_KillsTarget_AndReportsStalePidWithoutAborting()
        {
            using var victim = WinProcess.Start(new WinProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping -n 30 127.0.0.1 > nul",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            victim.ShouldNotBeNull();
            int stalePid = GetUnusedPid();

            try
            {
                var summary = _ops.TerminateProcesses([victim.Id, stalePid]);

                summary.SucceededPids.ShouldBe([victim.Id]);
                var failure = summary.Failures.ShouldHaveSingleItem();
                failure.Pid.ShouldBe(stalePid);
                victim.WaitForExit(5_000).ShouldBeTrue();
                victim.HasExited.ShouldBeTrue();
            }
            finally
            {
                if (!victim.HasExited)
                {
                    try { victim.Kill(); } catch { /* best-effort cleanup */ }
                }
            }
        }

        private static int GetUnusedPid()
        {
            var livePids = WinProcess.GetProcesses().Select(p => p.Id).ToHashSet();
            for (int pid = 4; pid < 100_000; pid++)
            {
                if (!livePids.Contains(pid))
                {
                    return pid;
                }
            }

            throw new InvalidOperationException("Could not find an unused PID.");
        }
    }
}
```

- [ ] **Step 3: Delete the old integration test file**

`git rm tests/TaskManager.IntegrationTests/ProcessManagement/ProcessManagerTests.cs` (its hermetic half lives in `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs` since Task 3; its ops half is replaced above).

- [ ] **Step 4: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.IntegrationTests
rg -n "ProcessManager|TimerManager" src tests --glob "*.cs" | rg -v "ProcessOperationsService"
# expect: no output
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: dissolve ProcessManager and TimerManager; integration suite targets ProcessOperationsService"
```

---

### Task 6: Full verification sweep + README architecture note

Final gate: everything green, boundaries provably clean, docs match reality.

**Files:**
- Modify: `README.md` (solution-layout paragraph only)

- [ ] **Step 1: Clean full build + both suites**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.IntegrationTests
```

All three must pass. If the integration suite is impractical in the current environment, note the skip explicitly in the completion report instead of silently ignoring it.

- [ ] **Step 2: Boundary greps (spec enforcement)**

```bash
rg -n "IServiceProvider|GetRequiredService" src/TaskManager --glob "*.cs" | rg -v "App.xaml.cs"
# expect: no output — service location confined to composition root

rg -n "System.Windows|ObservableCollection|Dispatcher" src/TaskManager.Domain --glob "*.cs"
# expect: no output — Domain contains no presentation concepts

rg -n "class WindowService|IWindowService|IProcessListCatalog" src/TaskManager --glob "*.cs"
# expect: interface + impl + VM consumers, no direct 'new .*Window()' outside WindowService/App
```

- [ ] **Step 3: Update README solution layout**

In `README.md`, replace the two-line project descriptions under `Solution layout` with:

```
src/
  TaskManager/           WPF application: views, view models, presentation state
                         (ProcessListCatalog), window orchestration (IWindowService),
                         app services, localization
  TaskManager.Domain/    UI-free core: process snapshots/diff engine, enrichment,
                         OS process operations, exporters, settings model
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: describe presentation/core split in README solution layout"
```

---

## Verification Matrix (spec requirement → task)

| Spec requirement | Task |
|---|---|
| Fix F5 double `SettingsService` registration | 1 |
| `IDispatcherService` leaves Domain | 2 |
| `ProcessListCatalog` owns view state/pipeline/batch apply | 3 |
| Polling via PeriodicTimer, no `async void`, skip-if-busy, manual-waits, restart-after-manual | 3 |
| Kill/set-priority delegated to `ProcessOperationsService` with writeback inside catalog | 3 |
| VMs: no `IServiceProvider`, explicit interfaces only | 4 |
| `IWindowService` owns all window construction/dialogs; `GetAssociatedWindow` dies | 4 |
| Factories deleted; exporter creation in composition root (delegate) | 4 |
| Startup via explicit `InitializeCommand` from composition root, no ctor side effects | 4 |
| Partial-failure formatting deduplicated | 4 |
| `Preconditions` flags enum → plain guard | 4 |
| `FolderSelector` → `IFolderPicker` (cancel clears, UX preserved) | 4 |
| INPC on `Process` retained (spec §1 decision) | unchanged throughout |
| `ProcessManager`/`TimerManager` deleted; integration tests target ops service | 5 |
| Domain free of `TaskManager.Abstractions` references | 5 |
| Full suites green; boundary greps; README updated | 6 |

## Execution Notes (as-built deviations)

Recorded during execution on branch `refactor/project-structure-consolidation`:

1. **Task 2 sequencing corrected.** Domain cannot reference `TaskManager.Abstractions` (the app project depends on Domain — a back-reference is impossible). Executed as: app-level `IDispatcherService` introduced immediately; `WpfDispatcherService` implemented *both* interfaces transitionally with a forwarding DI registration; the Domain interface and forwarding registration were deleted together with `ProcessManager` in Task 5.
2. **`IProcessOperations` extracted** (`Domain/Abstractions`) and consumed by the catalog. NSubstitute cannot intercept non-virtual methods of the concrete `ProcessOperationsService`, which made catalog unit tests execute real OS calls. The interface is the honest DIP fix; integration tests still target the concrete class.
3. **`TryExport(DataType)` signature** dropped the unused-after-validation `ExportationType` parameter entirely (plan had kept it).
4. **Export tests use a hand-rolled factory fake** instead of `Substitute.For<Func<…>>()` — NSubstitute failed to record delegate invocations in this setup (`CouldNotSetReturnDueToNoLastCallException`); the dictionary-backed fake is deterministic.
5. **Known environmental flake (pre-existing):** `ClipboardCopyTests.CopyMultipleRows_JoinsFormatterOutputWithNewlines` reads the live system clipboard (its own header documents cross-app contention). It failed intermittently during execution and passed on all dedicated re-runs; not a redesign regression.
