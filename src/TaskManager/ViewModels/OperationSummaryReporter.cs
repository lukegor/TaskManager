using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;

namespace TaskManager.ViewModels
{
    /// <summary>Single formatting point for partial-failure batch reports.</summary>
    internal static class OperationSummaryReporter
    {
        public static string FormatPartialFailures(ProcessOpSummary summary) =>
            string.Format(Strings.OpsCompletedWithFailuresFormat,
                summary.SucceededPids.Count,
                summary.SucceededPids.Count + summary.Failures.Count);
    }
}
