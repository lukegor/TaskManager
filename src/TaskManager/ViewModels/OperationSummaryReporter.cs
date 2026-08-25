using System.Globalization;
using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;

namespace TaskManager.ViewModels
{
    /// <summary>Single formatting point for partial-failure batch reports.</summary>
    internal static class OperationSummaryReporter
    {
        public static string FormatPartialFailures(ProcessOpSummary summary) =>
#pragma warning disable CA1863 // format string is culture-resolved localization (Strings.*); caching a CompositeFormat would freeze one UI language
            string.Format(CultureInfo.CurrentCulture, Strings.OpsCompletedWithFailuresFormat,
                summary.SucceededPids.Count,
                summary.SucceededPids.Count + summary.Failures.Count);
#pragma warning restore CA1863
    }
}
