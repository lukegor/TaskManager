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
            Assert.Equal(ProcessPriorityClass.AboveNormal, _self.PriorityClass);
            Assert.Equal(10, item.Process.Priority); // AboveNormal => base priority 10
        }

        [Fact]
        public void SetPriority_StalePid_DoesNotAbortRemainingUpdates()
        {
            int stalePid = GetUnusedPid();
            var item = GivenTrackedProcess(_self.Id);

            // a process can die between selection and confirmation; the rest of the batch must survive it
            _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.BelowNormal);

            _self.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, _self.PriorityClass);
            Assert.Equal(6, item.Process.Priority); // BelowNormal => base priority 6
        }

        [Fact]
        public void SetPriority_StalePid_IsReportedInSummary()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid }, ProcessPriorityClass.Normal);

            Assert.Empty(summary.SucceededPids);
            var failure = Assert.Single(summary.Failures);
            Assert.Equal(stalePid, failure.Pid);
            Assert.Equal(ProcessOpFailureReason.ProcessExited, failure.Reason);
        }

        [Fact]
        public void SetPriority_MixedBatch_ReportsBothOutcomesAndSurvives()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.AboveNormal);

            Assert.Equal(new[] { _self.Id }, summary.SucceededPids);
            Assert.Single(summary.Failures);
            _self.Refresh();
            Assert.Equal(ProcessPriorityClass.AboveNormal, _self.PriorityClass);
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
            Assert.NotNull(victim);
            int stalePid = GetUnusedPid();

            try
            {
                var summary = _manager.TerminateProcesses(new[] { victim.Id, stalePid });

                Assert.Equal(new[] { victim.Id }, summary.SucceededPids);
                var failure = Assert.Single(summary.Failures);
                Assert.Equal(stalePid, failure.Pid);
                Assert.True(victim.WaitForExit(5_000));
                Assert.True(victim.HasExited);
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
