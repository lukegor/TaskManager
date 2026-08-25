using System.ComponentModel;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Services
{
    /// <summary>
    /// Drives ExecutePerPid directly (internal, visible to this project): the OS-facing
    /// public methods stay integration-test territory, while summary/classification
    /// logging is verified hermetically.
    /// </summary>
    public class ProcessOperationsServiceLoggingTests
    {
        private readonly RecordingLogger<ProcessOperationsService> _logger = new();
        private readonly ProcessOperationsService _service;

        public ProcessOperationsServiceLoggingTests()
        {
            _service = new ProcessOperationsService(_logger);
        }

        [Fact]
        public void ExecutePerPid_MixedOutcomes_LogsStructuredSummary()
        {
            var summary = _service.ExecutePerPid("terminate", [1, 2, 3], pid =>
            {
                if (pid == 2)
                {
                    throw new Win32Exception(5, "access denied");
                }
            });

            summary.SucceededPids.ShouldBe([1, 3]);

            var info = _logger.Entries.Single(e => e.Level == LogLevel.Information);
            info.Message.ShouldBe("Batch terminate completed: 2 succeeded, 1 failed");
            info.Values["Operation"].ShouldBe("terminate");
            info.Values["SucceededCount"].ShouldBe(2);
            info.Values["FailedCount"].ShouldBe(1);
        }

        [Fact]
        public void ExecutePerPid_AllSucceed_SummaryReportsZeroFailures_AndNoFailureDebug()
        {
            _service.ExecutePerPid("set-priority", [7, 8], _ => { });

            var info = _logger.Entries.Single(e => e.Level == LogLevel.Information);
            info.Message.ShouldBe("Batch set-priority completed: 2 succeeded, 0 failed");
            _logger.Entries.Count(e => e.Level == LogLevel.Debug).ShouldBe(0);
        }

        [Fact]
        public void ExecutePerPid_WithFailures_LogsEachFailureAtDebug()
        {
            _service.ExecutePerPid("set-priority", [9], _ =>
                throw new ArgumentException("process exited"));

            var debugs = _logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
            debugs.Count.ShouldBe(1);
            debugs[0].Message.ShouldBe("Batch set-priority: PID 9 failed (ProcessExited)");
            debugs[0].Values["Operation"].ShouldBe("set-priority");
            debugs[0].Values["Pid"].ShouldBe(9);
            debugs[0].Values["Reason"].ShouldBe(ProcessOpFailureReason.ProcessExited);
        }
    }
}
