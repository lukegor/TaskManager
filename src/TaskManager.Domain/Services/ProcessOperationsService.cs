using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    public class ProcessOperationsService : IProcessOperations
    {
        private readonly ILogger<ProcessOperationsService> _logger;

        public ProcessOperationsService(ILogger<ProcessOperationsService> logger)
        {
            _logger = logger;
        }

        public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Process {Pid} was terminated", pid);
                }
            });
        }

        public ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.PriorityClass = priority;
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Process {Pid} priority set to {Priority}", pid, priority);
                }
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
