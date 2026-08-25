using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.ComponentModel;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;
using TaskManager.Presentation;
using IDispatcherService = TaskManager.Abstractions.IDispatcherService;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

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
        private readonly IProcessOperations _ops = Substitute.For<IProcessOperations>();
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
            _settings.Current.Returns(high);                             // service swaps snapshot first
            _settings.Changed += Raise.Event<Action<AppSettings>>(high); // then notifies consumers

            _catalog.CurrentIntervalSeconds.ShouldBe(5); // High => 5s
        }

        // ---- batch operation delegation + writeback ----

        [Fact]
        public async Task TerminateProcessesAsync_DelegatesToOpsService()
        {
            _ops.TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>())
                .Returns(ProcessOpSummary.Empty);

            await _catalog.TerminateProcessesAsync([42]);

            _ops.Received(1).TerminateProcesses(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 42));
        }

        [Fact]
        public async Task SetPriorityAsync_SuccessfulPids_AreWrittenBackIntoStoredRows()
        {
            _enumerator.Queue(Snap(9));
            await _catalog.LoadForTestAsync();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary { SucceededPids = [9], Failures = [] });

            var summary = await _catalog.SetPriorityAsync([9], ProcessPriorityClass.AboveNormal);

            summary.HasFailures.ShouldBeFalse();
            _catalog.Items.Single().Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public async Task SetPriorityAsync_FailedPids_AreNotWrittenBack()
        {
            _enumerator.Queue(Snap(9));
            await _catalog.LoadForTestAsync();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary
                {
                    SucceededPids = [],
                    Failures = [new ProcessOpFailure(9, ProcessOpFailureReason.AccessDenied)]
                });

            await _catalog.SetPriorityAsync([9], ProcessPriorityClass.BelowNormal);

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
