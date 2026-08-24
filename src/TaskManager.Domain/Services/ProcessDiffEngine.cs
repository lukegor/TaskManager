using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    public sealed record ProcessListDiff(
        IReadOnlyList<ProcessSnapshot> Added,
        IReadOnlyList<int> Removed,
        IReadOnlyList<ProcessSnapshot> Updated)
    {
        public static readonly ProcessListDiff Empty = new([], [], []);
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Updated.Count == 0;
    }

    /// <summary>
    /// Pure function: reconcile last-applied store state against a fresh snapshot.
    /// </summary>
    public static class ProcessDiffEngine
    {
        public static ProcessListDiff Compute(
            IReadOnlyDictionary<int, Process> current,
            IReadOnlyList<ProcessSnapshot> snapshot)
        {
            List<ProcessSnapshot> added = [];
            List<int> removed = [];
            List<ProcessSnapshot> updated = [];

            HashSet<int> snapshotted = new(snapshot.Select(s => s.Pid));

            foreach (var s in snapshot)
            {
                if (!current.TryGetValue(s.Pid, out var stored))
                {
                    added.Add(s);
                    continue;
                }

                if (stored.Name != s.Name ||
                    stored.ThreadCount != s.ThreadCount ||
                    stored.Priority != s.BasePriority ||
                    stored.Ppid != s.Ppid)
                {
                    updated.Add(s);
                }
            }

            removed.AddRange(current.Keys.Where(pid => !snapshotted.Contains(pid)).OrderBy(pid => pid));

            return new ProcessListDiff(added, removed, updated);
        }
    }
}
