using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Presentation;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Presentation
{
    public class ProcessListCatalogDiagnosticsTests
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly IProcessOperations _ops = Substitute.For<IProcessOperations>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly FakeTimeProvider _time = new();

        private ProcessListCatalog CreateCatalog(RefreshFrequencyType frequency = RefreshFrequencyType.Low)
        {
            _settings.Current.Returns(AppSettings.Defaults with { ProcessesRefreshFrequency = frequency });
            return new ProcessListCatalog(
                InlineDispatcher, _enumerator, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);
        }

        private void SettingsChangedTo(RefreshFrequencyType frequency)
        {
            var settings = AppSettings.Defaults with { ProcessesRefreshFrequency = frequency };
            _settings.Current.Returns(settings);
            _settings.Changed += Raise.Event<Action<AppSettings>>(settings);
        }

        [Fact]
        public async Task SuccessfulFill_PublishesOkDiagnostics()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));

            var raises = 0;
            catalog.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessListCatalog.LastRefresh))
                {
                    raises++;
                }
            };

            await catalog.LoadForTestAsync();

            catalog.LastRefresh.ShouldNotBeNull();
            catalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Ok);
            catalog.LastRefresh.LastDurationMs.ShouldBeGreaterThanOrEqualTo(0);
            raises.ShouldBe(1);
        }

        [Fact]
        public async Task GateBusy_SecondAttempt_PublishesSkipped_ThenFirstCompletesOk()
        {
            var catalog = CreateCatalog();
            var gateOpened = new TaskCompletionSource();

            // Replace the injected enumerator with a gated one so the FIRST attempt
            // holds the refresh gate deterministically until we release it.
            var gatedCatalog = new ProcessListCatalog(
                InlineDispatcher,
                new GatedEnumerator(gateOpened.Task),
                new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);

            var observedOutcomes = new List<RefreshOutcome>();
            var observedSkippedDuration = double.MinValue;
            gatedCatalog.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessListCatalog.LastRefresh)
                    && gatedCatalog.LastRefresh is { } snapshot)
                {
                    observedOutcomes.Add(snapshot.Outcome);
                    if (snapshot.Outcome == RefreshOutcome.Skipped)
                    {
                        observedSkippedDuration = snapshot.LastDurationMs;
                    }
                }
            };

            var first = gatedCatalog.LoadForTestAsync();  // occupies the gate
            await gatedCatalog.LoadForTestAsync();        // busy -> Skipped published inline

            observedOutcomes.ShouldBe([RefreshOutcome.Skipped]);   // regression guard under test
            observedSkippedDuration.ShouldBe(0);                   // no completed Ok yet: carries 0

            gateOpened.SetResult();
            await first;

            observedOutcomes.ShouldBe([RefreshOutcome.Skipped, RefreshOutcome.Ok]);
            gatedCatalog.LastRefresh!.LastDurationMs.ShouldBeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task EnumeratorThrows_PublishesFailed_AndDoesNotThrow()
        {
            var throwing = new ThrowingEnumerator();
            var failingCatalog = new ProcessListCatalog(
                InlineDispatcher, throwing, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);

            await failingCatalog.LoadForTestAsync(); // must not propagate

            failingCatalog.LastRefresh.ShouldNotBeNull();
            failingCatalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Failed);
        }

        [Fact]
        public async Task PausedSetting_FlipsIsPollingPaused()
        {
            var catalog = CreateCatalog(RefreshFrequencyType.High);
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.LoadForTestAsync();

            SettingsChangedTo(RefreshFrequencyType.Paused);
            catalog.IsPollingPaused.ShouldBeTrue();

            SettingsChangedTo(RefreshFrequencyType.High);
            catalog.IsPollingPaused.ShouldBeFalse();
        }

        private static readonly IDispatcherService InlineDispatcher = MakeInlineDispatcher();

        private static IDispatcherService MakeInlineDispatcher()
        {
            var d = Substitute.For<IDispatcherService>();
            d.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ((Action)ci[0])());
            return d;
        }

        private sealed class GatedEnumerator(Task gate) : ISystemProcessEnumerator
        {
            public IReadOnlyList<ProcessSnapshot> Capture()
            {
                if (!gate.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("gate was never opened - test sequencing broke");
                }

                return [ProcessFakes.Snap(99)];
            }
        }

        private sealed class ThrowingEnumerator : ISystemProcessEnumerator
        {
            public IReadOnlyList<ProcessSnapshot> Capture() =>
                throw new InvalidOperationException("boom");
        }
    }
}
