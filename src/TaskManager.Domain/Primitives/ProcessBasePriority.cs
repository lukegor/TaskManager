using System.Diagnostics;

namespace TaskManager.Domain.Primitives
{
    public static class ProcessBasePriority
    {
        private static readonly Dictionary<ProcessPriorityClass, int> BasePriorityMap = new Dictionary<ProcessPriorityClass, int>
        {
            { ProcessPriorityClass.Idle, 4 },
            { ProcessPriorityClass.BelowNormal, 6 },
            { ProcessPriorityClass.Normal, 8 },
            { ProcessPriorityClass.AboveNormal, 10 },
            { ProcessPriorityClass.High, 13 },
            { ProcessPriorityClass.RealTime, 24 },
        };

        /// <summary>Maps a ProcessPriorityClass to its Windows base priority value.</summary>
        public static int Get(ProcessPriorityClass priority)
        {
            return BasePriorityMap.TryGetValue(priority, out int basePriority)
                ? basePriority
                : (int)priority;
        }
    }
}
