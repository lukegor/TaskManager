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
    /// Polling-loop dynamics driven by a FakeTimeProvider: ticks occur only when the fake
    /// clock advances, so interval changes, pause/resume, and phase restarts are verified
    /// without real delays.
    /// </summary>
    public class ProcessListCatalogPollingTests
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
        public async Task FirstTick_HappensExactlyAfterOneInterval()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));

            await catalog.InitializeAsync();
            _enumerator.CallCount.ShouldBe(1); // initial fill only

            _time.Advance(TimeSpan.FromSeconds(9)); // below the Low interval (10s)
            _enumerator.CallCount.ShouldBe(1);

            _time.Advance(TimeSpan.FromSeconds(1)); // crosses 10s
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);
        }

        [Fact]
        public async Task IntervalChange_TakesEffectOnNextPeriod()
        {
            var catalog = CreateCatalog(); // Low = 10s
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            SettingsChangedTo(RefreshFrequencyType.High); // 5s

            _time.Advance(TimeSpan.FromSeconds(5)); // new period; old 10s period not yet due
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);

            _time.Advance(TimeSpan.FromSeconds(5));
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 3);
        }

        [Fact]
        public async Task Paused_StopsTicking_ResumeStartsFreshLoop()
        {
            var catalog = CreateCatalog(); // Low
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            SettingsChangedTo(RefreshFrequencyType.Paused);

            _time.Advance(TimeSpan.FromMinutes(1));
            _enumerator.CallCount.ShouldBe(1); // paused: idle

            _enumerator.Queue(ProcessFakes.Snap(1));
            SettingsChangedTo(RefreshFrequencyType.Low);

            _time.Advance(TimeSpan.FromSeconds(10));
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2); // fresh loop ticking again
        }

        [Fact]
        public async Task ManualRefresh_RestartsPollingPhase()
        {
            var catalog = CreateCatalog(); // Low = 10s
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            _time.Advance(TimeSpan.FromSeconds(8)); // near the end of the first period
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.PerformRefreshAsync(isUserInitiated: true); // capture #2 + phase restart

            _time.Advance(TimeSpan.FromSeconds(9)); // would have ticked at t=10 pre-restart
            _enumerator.CallCount.ShouldBe(2);

            _time.Advance(TimeSpan.FromSeconds(1)); // 10s after the manual completion
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 3);
        }

        [Fact]
        public async Task PollingTick_DuringInFlightManualRefresh_IsSkipped()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync(); // capture #1

            // a manual refresh whose capture blocks until we release it
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            var manual = catalog.PerformRefreshAsync(isUserInitiated: true);

            // deterministic precondition: the manual capture has ENTERED the enumerator
            // (CallCount counts entries) and is now parked on the release signal, holding the gate
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);

            _time.Advance(TimeSpan.FromSeconds(10)); // polling tick fires while the gate is held
            _enumerator.CallCount.ShouldBe(2);       // skipped: no new capture entered the enumerator

            releaseFirst.SetResult();
            await manual;

            _enumerator.CallCount.ShouldBe(2); // still exactly two captures total
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
