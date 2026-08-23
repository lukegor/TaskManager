using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using WinProcess = System.Diagnostics.Process;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

namespace TaskManager.Tests
{
    /// <summary>
    /// Behavior contract of ProcessManager.SetPriority:
    /// every selected PID gets its real OS priority class changed, and the matching
    /// tracked ProcessItem gets its displayed base-priority value updated.
    /// The test process itself is used as the safely mutable target.
    /// </summary>
    public class ProcessManagerTests : IDisposable
    {
        private readonly ProcessManager _manager;
        private readonly WinProcess _self = WinProcess.GetCurrentProcess();
        private readonly ProcessPriorityClass _originalPriority;

        public ProcessManagerTests()
        {
            IAppSettings settings = Substitute.For<IAppSettings>();
            settings.RefreshFrequency.Returns(RefreshFrequencyType.Low); // timer is never started

            _manager = new ProcessManager(
                Substitute.For<IDispatcherService>(),
                settings,
                new TimerManager(settings));
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

        private ProcessItem GivenTrackedProcess(int pid)
        {
            var item = new ProcessItem(new Process { Name = "self", Pid = pid });
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
