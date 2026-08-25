using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    public class OperationSummaryReporterTests
    {
        [Fact]
        public void FormatPartialFailures_ReportsSucceededCountAndTotal()
        {
            var summary = new ProcessOpSummary
            {
                SucceededPids = [1, 2],
                Failures = [new ProcessOpFailure(3, ProcessOpFailureReason.AccessDenied)]
            };

            var formatted = OperationSummaryReporter.FormatPartialFailures(summary);

            formatted.ShouldBe(string.Format(Strings.OpsCompletedWithFailuresFormat, 2, 3));
        }

        [Fact]
        public void FormatPartialFailures_AllSucceeded_FormattedAnyway()
        {
            var summary = new ProcessOpSummary { SucceededPids = [1], Failures = [] };

            OperationSummaryReporter.FormatPartialFailures(summary)
                .ShouldBe(string.Format(Strings.OpsCompletedWithFailuresFormat, 1, 1));
        }
    }
}
