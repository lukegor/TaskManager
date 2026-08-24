using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    public class ProcessOperationsService
    {
        private readonly ILogger<ProcessOperationsService> _logger;

        public ProcessOperationsService(ILogger<ProcessOperationsService> logger)
        {
            _logger = logger;
        }

        public ProcessOpSummary TerminateProcesses(
            IReadOnlyCollection<int> pids,
            Action<int>? onSuccess = null)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
                _logger.LogDebug("Process {Pid} was terminated", pid);
                onSuccess?.Invoke(pid);
            });
        }

        public ProcessOpSummary SetPriority(
            IReadOnlyCollection<int> pids,
            ProcessPriorityClass priority,
            Action<int>? onSuccess = null)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.PriorityClass = priority;
                _logger.LogDebug("Process {Pid} priority set to {Priority}", pid, priority);
                onSuccess?.Invoke(pid);
            });
        }

        private ProcessOpSummary ExecutePerPid(IEnumerable<int> selectedPids, Action<int> operation)
        {
            var succeeded = new List<int>();
            var failures = new List<ProcessOpFailure>();

            foreach (var pid in selectedPids)
            {
                try
                {
                    operation(pid);
                    succeeded.Add(pid);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    var reason = ClassifyFailure(ex);
                    _logger.LogWarning(ex, "Process operation failed for PID {Pid} ({Reason})", pid, reason);
                    failures.Add(new ProcessOpFailure(pid, reason));
                }
            }

            return new ProcessOpSummary { SucceededPids = succeeded, Failures = failures };
        }

        private const int ErrorAccessDenied = 5;

        private static ProcessOpFailureReason ClassifyFailure(Exception ex) => ex switch
        {
            Win32Exception { NativeErrorCode: ErrorAccessDenied } => ProcessOpFailureReason.AccessDenied,
            ArgumentException => ProcessOpFailureReason.ProcessExited,
            InvalidOperationException => ProcessOpFailureReason.ProcessExited,
            _ => ProcessOpFailureReason.Unknown
        };
    }
}
