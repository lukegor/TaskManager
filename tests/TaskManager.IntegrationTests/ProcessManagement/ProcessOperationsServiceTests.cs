using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using TaskManager.Domain.Abstractions;
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
