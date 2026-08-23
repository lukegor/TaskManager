using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using WinProcess = System.Diagnostics.Process;
using WinProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

namespace TaskManager.Tests
{
    /// <summary>
    /// Behavior contract of ProcessManager batch operations:
    /// every selected PID gets its real OS operation applied, expected failures are
    /// reported as data without aborting the batch, and the matching tracked
    /// ProcessItem gets its displayed base-priority value updated.
    /// The test process itself is used as the safely mutable target.
    /// </summary>
    public class ProcessManagerTests : IDisposable
    {
        private readonly ProcessManager _manager;
        private readonly IAppSettings _settings;
        private readonly WinProcess _self = WinProcess.GetCurrentProcess();
        private readonly ProcessPriorityClass _originalPriority;

        public ProcessManagerTests()
        {
            _settings = Substitute.For<IAppSettings>();
            _settings.RefreshFrequency.Returns(RefreshFrequencyType.Low); // timer is never started

            _manager = new ProcessManager(
                Substitute.For<IDispatcherService>(),
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);
            _originalPriority = _self.PriorityClass;
        }

        public void Dispose()
        {
            _self.PriorityClass = _originalPriority;
        }

        [Fact]
        public void SetPriority_UpdatesRealProcessAndStoredModel()
        {
            var item = GivenTrackedProcess(_self.Id);

            _manager.SetPriority(new[] { _self.Id }, ProcessPriorityClass.AboveNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
            item.Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public void SetPriority_StalePid_DoesNotAbortRemainingUpdates()
        {
            int stalePid = GetUnusedPid();
            var item = GivenTrackedProcess(_self.Id);

            // a process can die between selection and confirmation; the rest of the batch must survive it
            _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.BelowNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.BelowNormal);
            item.Process.Priority.ShouldBe(6); // BelowNormal => base priority 6
        }

        [Fact]
        public void SetPriority_StalePid_IsReportedInSummary()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid }, ProcessPriorityClass.Normal);

            summary.SucceededPids.ShouldBeEmpty();
            var failure = summary.Failures.ShouldHaveSingleItem();
            failure.Pid.ShouldBe(stalePid);
            failure.Reason.ShouldBe(ProcessOpFailureReason.ProcessExited);
        }

        [Fact]
        public void SetPriority_MixedBatch_ReportsBothOutcomesAndSurvives()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.AboveNormal);

            summary.SucceededPids.ShouldBe(new[] { _self.Id });
            summary.Failures.ShouldHaveSingleItem();
            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
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
        public async Task LoadProcesses_PopulatesCollectionThroughDispatcher()
        {
            var dispatcher = Substitute.For<IDispatcherService>();
            // execute inline like the real UI dispatcher would
            dispatcher.When(d => d.Invoke(Arg.Any<Action>()))
                .Do(ci => ((Action)ci[0])());
            var manager = new ProcessManager(
                dispatcher,
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);

            await manager.LoadProcesses();

            manager.Processes.ShouldNotBeEmpty();
            dispatcher.Received().Invoke(Arg.Any<Action>());
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
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);
            failingManager.Processes.Add(
                new ProcessItem(new Process { Name = "ghost", Pid = GetUnusedPid(), Path = string.Empty }));

            await failingManager.SafePollingRefreshAsync();
        }

        private ProcessItem GivenTrackedProcess(int pid)
        {
            var item = new ProcessItem(new Process { Name = "self", Pid = pid, Path = string.Empty });
            _manager.Processes.Add(item);
            return item;
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


