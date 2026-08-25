using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Presentation;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Presentation
{
    /// <summary>
    /// The once-per-refresh Debug trace is the designated data source for a future
    /// diagnostics window; these tests pin its guarded emission and diff counts.
    /// </summary>
    public class ProcessListCatalogRefreshTraceTests
    {
        [Fact]
        public async Task RefreshPipeline_EmitsGuardedTraceWithDiffCounts()
        {
            var enumerator = new ScriptedEnumerator();
            enumerator.Queue(ProcessFakes.Snap(1));
            enumerator.Queue(ProcessFakes.Snap(2));
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Defaults);
            var logger = new RecordingLogger<ProcessListCatalog>();

            var catalog = new ProcessListCatalog(
                InlineDispatcher, enumerator, new CountingEnricher(), settings,
                Substitute.For<IProcessOperations>(), new FakeTimeProvider(), logger);

            await catalog.LoadForTestAsync();
            await catalog.LoadForTestAsync();

            var traces = logger.Entries
                .Where(e => e.Level == LogLevel.Debug
                            && e.Message.StartsWith("Refresh completed", StringComparison.Ordinal))
                .ToList();

            traces.Count.ShouldBe(2);
            traces[0].Values["AddedCount"].ShouldBe(1);
            traces[0].Values.ContainsKey("ElapsedMs").ShouldBeTrue();

            // second fill swaps pid 1 out for pid 2
            traces[1].Values["AddedCount"].ShouldBe(1);
            traces[1].Values["RemovedCount"].ShouldBe(1);
        }

        private static readonly IDispatcherService InlineDispatcher = MakeInlineDispatcher();

        private static IDispatcherService MakeInlineDispatcher()
        {
            var d = Substitute.For<IDispatcherService>();
            d.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ((Action)ci[0])());
            return d;
        }
    }
}
