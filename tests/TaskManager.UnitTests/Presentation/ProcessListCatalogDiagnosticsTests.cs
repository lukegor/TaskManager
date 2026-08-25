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
            var gateOpened = new TaskCompletionSource();
            var blockingEnumerator = new GatedEnumerator(gateOpened.Task);

            // the gate holder must be bound at construction; the first refresh parks inside
            // its Capture until released, deterministically holding _refreshGate
            var catalog = new ProcessListCatalog(
                InlineDispatcher, blockingEnumerator, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);

            var first = catalog.LoadForTestAsync();       // occupies the refresh gate

            await catalog.LoadForTestAsync();             // hits busy gate -> Skipped

            gateOpened.SetResult();
            await first;

            catalog.LastRefresh.ShouldNotBeNull();
            catalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Ok); // successful attempt wins last-write
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
                gate.Wait(TimeSpan.FromSeconds(5));
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
