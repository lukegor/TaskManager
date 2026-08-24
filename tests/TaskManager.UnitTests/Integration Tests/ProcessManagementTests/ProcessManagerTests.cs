using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Domain.Primitives;
using WinProcess = System.Diagnostics.Process;
using WinProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

namespace TaskManager.Tests
{
    /// <summary>
    /// Behavior contract of ProcessManager: snapshot-driven refresh batches (add/update/remove,
    /// in-place instance reuse), reentrancy guards, batch ops reporting failures as data, and
    /// materialized export snapshots immune to later store changes.
    /// A scripted enumerator drives the pipeline; the real OS process is used as the safely
    /// mutable target for batch operations.
    /// </summary>
    public class ProcessManagerTests : IDisposable
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly ProcessManager _manager;
        private readonly ISettingsService _settings;
        private readonly TimerManager _timer;
        private readonly WinProcess _self = WinProcess.GetCurrentProcess();
        private readonly ProcessPriorityClass _originalPriority;
        private readonly IDispatcherService _inlineDispatcher;

        public ProcessManagerTests()
        {
            _settings = Substitute.For<ISettingsService>();
            _settings.Current.Returns(new AppSettings
            {
                Language = "English",
                ProcessesRefreshFrequency = RefreshFrequencyType.Low, // timer is never started
                DateTimeFormat = AppSettings.Defaults.DateTimeFormat
            });

            _inlineDispatcher = Substitute.For<IDispatcherService>();
            _inlineDispatcher.When(d => d.Invoke(Arg.Any<Action>()))
                .Do(ci => ((Action)ci[0])());

            _timer = new TimerManager(_settings);
            _manager = new ProcessManager(
                _inlineDispatcher,
                _enumerator,
                new ProcessEnricher(NullLogger<ProcessEnricher>.Instance),
                _settings,
                _timer,
                NullLogger<ProcessManager>.Instance);
            _originalPriority = _self.PriorityClass;
        }

        public void Dispose() => _self.PriorityClass = _originalPriority;

        // ---- refresh pipeline ----

        [Fact]
        public async Task FirstRefresh_AddsEverythingFromSnapshot()
        {
            _enumerator.Queue(Snap(1, "alpha"), Snap(2, "beta"));

            await _manager.LoadProcesses();

            _manager.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 1, 2 });
            _manager.ProcessCount.ShouldBe(2);
        }

        [Fact]
        public async Task SecondRefresh_UpdatesExistingInstance_InPlace_AndRaisesFieldNotification()
        {
            _enumerator.Queue(Snap(1, "alpha", threadCount: 3));
            await _manager.LoadProcesses();
            var original = _manager.Items.Single();
            var notified = new List<string>();
            original.Process.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            _enumerator.Queue(Snap(1, "alpha", threadCount: 7));
            await _manager.PerformRefresh(isUserInitiated: false);

            _manager.Items.ShouldHaveSingleItem().ShouldBe(original); // same instance -> selection survives
            original.Process.ThreadCount.ShouldBe(7);
            notified.ShouldContain(nameof(Process.ThreadCount));
        }

        [Fact]
        public async Task SecondRefresh_RemovesVanished_AndAddsNew()
        {
            _enumerator.Queue(Snap(1), Snap(2));
            await _manager.LoadProcesses();

            _enumerator.Queue(Snap(2), Snap(3));
            await _manager.PerformRefresh(isUserInitiated: false);

            _manager.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 2, 3 });
        }

        [Fact]
        public async Task RemovedPid_ReEnrichedOnReuse_EvictsCacheEntry()
        {
            var enumerator = new ScriptedEnumerator();
            var enricher = new CountingEnricher();
            var manager = new ProcessManager(
                _inlineDispatcher,
                enumerator,
                enricher,
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);

            enumerator.Queue(Snap(7));
            await manager.LoadProcesses();

            enumerator.Queue(); // PID 7 exits
            await manager.PerformRefresh(isUserInitiated: false);

            enumerator.Queue(Snap(7)); // PID 7 reused by another image
            await manager.PerformRefresh(isUserInitiated: false);

            enricher.Calls.ShouldBe(2); // second capture proves the cache entry was evicted, not replayed
        }

        // ---- reentrancy guard ----

        [Fact]
        public async Task OverlappingPollingRefresh_SecondCallSkips_FirstCompletes()
        {
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            _enumerator.Queue(Array.Empty<ProcessSnapshot>);

            var first = _manager.SafePollingRefreshAsync();
            var second = _manager.SafePollingRefreshAsync(); // must not queue behind, must skip

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

            var first = _manager.SafePollingRefreshAsync();
            var manual = _manager.PerformRefresh(isUserInitiated: true);

            manual.IsCompleted.ShouldBeFalse();
            releaseFirst.SetResult();
            await manual;

            _enumerator.CallCount.ShouldBe(2);
            _manager.ProcessCount.ShouldBe(1);
        }

        // ---- export snapshot ----

        [Fact]
        public async Task ExportSnapshot_IsImmuneToLaterStoreChanges()
        {
            _enumerator.Queue(Snap(1, "one"), Snap(2, "two"));
            await _manager.LoadProcesses();

            var snapshot = _manager.SnapshotForExport();

            _enumerator.Queue();
            await _manager.PerformRefresh(isUserInitiated: false); // everything exits

            _manager.Items.ShouldBeEmpty();
            snapshot.Count.ShouldBe(2);
            snapshot.Single(p => p.Pid == 1).Name.ShouldBe("one");
        }

        // ---- batch operations (existing contracts preserved) ----

        [Fact]
        public void SettingsChanged_AppliesNewPollingIntervalImmediately()
        {
            _timer.Interval.ShouldBe(10_000); // Low => 10s, from Current snapshot

            var high = AppSettings.Defaults with
            {
                ProcessesRefreshFrequency = RefreshFrequencyType.High
            };
            _settings.Changed += Raise.Event<Action<AppSettings>>(high);

            _timer.Interval.ShouldBe(5_000); // High => 5s
        }

        [Fact]
        public async Task SetPriority_UpdatesRealProcessAndStoredModel()
        {
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            _manager.SetPriority(new[] { _self.Id }, ProcessPriorityClass.AboveNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
            _manager.Items.Single().Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public async Task SetPriority_StalePid_DoesNotAbortRemainingUpdates()
        {
            int stalePid = GetUnusedPid();
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            // a process can die between selection and confirmation; the rest of the batch must survive it
            _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.BelowNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.BelowNormal);
            _manager.Items.Single().Process.Priority.ShouldBe(6); // BelowNormal => base priority 6
        }

        [Fact]
        public async Task SetPriority_StalePid_IsReportedInSummary()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid }, ProcessPriorityClass.Normal);

            summary.SucceededPids.ShouldBeEmpty();
            var failure = summary.Failures.ShouldHaveSingleItem();
            failure.Pid.ShouldBe(stalePid);
            failure.Reason.ShouldBe(ProcessOpFailureReason.ProcessExited);
        }

        [Fact]
        public async Task SetPriority_MixedBatch_ReportsBothOutcomesAndSurvives()
        {
            int stalePid = GetUnusedPid();
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            var summary = _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.AboveNormal);

            summary.SucceededPids.ShouldBe(new[] { _self.Id });
            summary.Failures.ShouldHaveSingleItem();
            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
        }

        [Fact]
        public async Task TerminateProcesses_KillsTarget_AndReportsStalePidWithoutAborting()
        {
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();
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
                var summary = _manager.TerminateProcesses(new[] { victim.Id, stalePid });

                summary.SucceededPids.ShouldBe(new[] { victim.Id });
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

        [Fact]
        public async Task SafePollingRefreshAsync_SwallowsAndLogsUnexpectedFailures()
        {
            var throwingDispatcher = Substitute.For<IDispatcherService>();
            throwingDispatcher
                .When(d => d.Invoke(Arg.Any<Action>()))
                .Do(_ => throw new InvalidOperationException("dispatcher died"));
            var failingManager = new ProcessManager(
                throwingDispatcher,
                ScriptedEnumerator.Of(SelfSnap()),
                new ProcessEnricher(NullLogger<ProcessEnricher>.Instance),
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);

            await failingManager.SafePollingRefreshAsync();
        }

        [Fact]
        public async Task EnumeratorFailure_IsSwallowedByPollingWrapper()
        {
            _enumerator.ThrowNext(new InvalidOperationException("snapshot boom"));

            await _manager.SafePollingRefreshAsync();

            _manager.Items.ShouldBeEmpty();
        }

        // ---- helpers ----

        private static ProcessSnapshot SelfSnap() =>
            new(Environment.ProcessId, "self", ThreadCount: 1, Ppid: null, BasePriority: 8);

        private static ProcessSnapshot Snap(int pid, string name = "n", int threadCount = 1) =>
            new(pid, name, ThreadCount: threadCount, Ppid: 4, BasePriority: 8);

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
