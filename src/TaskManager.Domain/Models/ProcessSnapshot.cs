namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Per-process data obtainable from one system call, before any handle is opened.
    /// </summary>
    public sealed record ProcessSnapshot(
        int Pid,
        string Name,
        int ThreadCount,
        int? Ppid,
        int? BasePriority);
}
