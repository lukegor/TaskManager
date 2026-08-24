using NtApiDotNet;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    /// <remarks>
    /// One system call returns name/PID/PPID/priority/thread-count for ALL processes,
    /// including protected ones that <see cref="System.Diagnostics.Process.GetProcesses"/>
    /// cannot open handles to.
    /// </remarks>
    public sealed class NtSystemProcessEnumerator : ISystemProcessEnumerator
    {
        public IReadOnlyList<ProcessSnapshot> Capture()
        {
            return NtSystemInfo.GetProcessInformation()
                .Select(p => new ProcessSnapshot(
                    p.ProcessId,
                    p.ImageName ?? string.Empty,
                    p.Threads?.Count() ?? 0,
                    p.ParentProcessId,
                    p.BasePriority))
                .ToList();
        }
    }
}
