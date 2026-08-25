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
    /// <summary>
    /// Shutdown contract of ProcessListCatalog: disposal is idempotent, no polling loop can
    /// start or resume after disposal, and a refresh caught mid-flight when the container
    /// disposes the catalog loses its race with the gate silently instead of throwing.
    /// </summary>
    public class ProcessListCatalogDisposeTests
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly IProcessOperations _ops = Substitute.For<IProcessOperations>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly FakeTimeProvider _time = new();

        private ProcessListCatalog CreateCatalog(RefreshFrequencyType frequency = RefreshFrequencyType.Low)
        {
            var current = AppSettings.Defaults with { ProcessesRefreshFrequency = frequency };
            _settings.Current.Returns(current);

            return new ProcessListCatalog(
                InlineDispatcher, _enumerator, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);
        }

        private void SettingsChangedTo(RefreshFrequencyType frequency)
        {
            var settings = AppSettings.Defaults with { ProcessesRefreshFrequency = frequency };
            _settings.Current.Returns(settings);                             // service swaps snapshot first
            _settings.Changed += Raise.Event<Action<AppSettings>>(settings); // then notifies consumers
        }

        [Fact]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var catalog = CreateCatalog();

            catalog.Dispose();

            catalog.Dispose(); // second pass must be a safe no-op, not ObjectDisposedException
        }

        [Fact]
        public async Task StartPolling_AfterDispose_DoesNotRestartLoop()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync(); // initial fill + live polling loop

            catalog.Dispose();

            catalog.StartPolling(); // must not resurrect the loop
            _time.Advance(TimeSpan.FromSeconds(10)); // would tick a live Low-period loop
            await Task.Delay(100, TestContext.Current.CancellationToken);                   // let any (regressed) tick surface

            _enumerator.CallCount.ShouldBe(1);
        }

        [Fact]
        public async Task SettingsChange_AfterDispose_DoesNotStartFreshLoop()
        {
            var catalog = CreateCatalog(); // Low = 10s
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            catalog.Dispose();

            SettingsChangedTo(RefreshFrequencyType.High); // pre-fix this restarts the loop
            _time.Advance(TimeSpan.FromSeconds(5));       // High period; would tick a live loop
            await Task.Delay(100, TestContext.Current.CancellationToken);

            _enumerator.CallCount.ShouldBe(1);
        }

        [Fact]
        public async Task Dispose_WhileLoopRefreshIsInFlight_TerminatesLoopWithoutThrowing()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync(); // capture #1, loop armed for t=10s

            var releaseTick = new TaskCompletionSource();
            _enumerator.Queue(() => releaseTick.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);

            _time.Advance(TimeSpan.FromSeconds(10)); // tick enters capture #2 and parks, holding the gate
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);

            catalog.Dispose(); // bounded-waits on the parked loop, then tears down; must not throw

            releaseTick.SetResult(); // drain the parked refresh against the now-disposed gate

            _time.Advance(TimeSpan.FromMinutes(1)); // no further ticks may reach the enumerator
            await Task.Delay(100, TestContext.Current.CancellationToken);
            _enumerator.CallCount.ShouldBe(2);
        }

        [Fact]
        public async Task ManualRefresh_InFlightDuringDispose_CompletesCleanly()
        {
            var catalog = CreateCatalog();
            var release = new TaskCompletionSource();
            _enumerator.Queue(() => release.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);

            var manual = catalog.PerformRefreshAsync(isUserInitiated: true);
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 1); // parked inside capture, holding the gate

            catalog.Dispose(); // gate disposed while the manual refresh still holds it
            release.SetResult();

            await manual; // must complete without surfacing the gate race
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
