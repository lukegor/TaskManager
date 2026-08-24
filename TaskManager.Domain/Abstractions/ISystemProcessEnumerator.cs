using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    /// <summary>
    /// Cheap whole-system process snapshot (no per-process handle opens).
    /// </summary>
    public interface ISystemProcessEnumerator
    {
        /// <exception cref="Exception">OS-level enumeration failure; callers treat as whole-tick failure.</exception>
        IReadOnlyList<ProcessSnapshot> Capture();
    }
}
