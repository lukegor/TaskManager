using System.Diagnostics;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    /// <summary>
    /// Batch operations against live OS processes. Expected OS rejections are reported
    /// as data (<see cref="ProcessOpSummary"/>), never as exceptions.
    /// </summary>
    public interface IProcessOperations
    {
        ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids, Action<int>? onSuccess = null);

        ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority, Action<int>? onSuccess = null);
    }
}
