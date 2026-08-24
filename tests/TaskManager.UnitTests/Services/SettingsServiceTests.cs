using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Services;
using TaskManager.Utility.Utility;

namespace TaskManager.Tests
{
    /// <summary>
    /// Contract of the settings mutation pipeline: validate → persist → swap → notify,
    /// with nothing persisted or announced when validation rejects the candidate.
    /// </summary>
    public class SettingsServiceTests
    {
        private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();

        private SettingsService CreateService() =>
            new(_store, NullLogger<SettingsService>.Instance);

        [Fact]
        public void Constructor_LoadsCurrentFromStore()
        {
            var stored = AppSettings.Defaults with { Language = "polski" };
            _store.Load().Returns(stored);

            CreateService().Current.ShouldBe(stored);
        }

        [Fact]
        public void Constructor_WithInvalidStoredSnapshot_YieldsDefaults()
        {
            var invalid = AppSettings.Defaults with { Language = "klingon" };
            _store.Load().Returns(invalid);

            CreateService().Current.ShouldBe(AppSettings.Defaults);
        }

        [Fact]
        public void Update_PersistsSwapsAndRaisesChangedWithSameSnapshot()
        {
            var service = CreateService();
            AppSettings? announced = null;
            service.Changed += s => announced = s;

            var candidate = AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.High };
            service.Update(candidate);

            _store.Received(1).Save(candidate);
            service.Current.ShouldBe(candidate);
            announced.ShouldBe(candidate);
        }

        [Fact]
        public void Update_WithInvalidCandidate_PersistsNothingAndRaisesNothing()
        {
            var service = CreateService();
            var raised = 0;
            service.Changed += _ => raised++;

            var invalid = AppSettings.Defaults with { Language = "deutsch" };
            var act = () => service.Update(invalid);

            Should.Throw<SettingsValidationException>(act);
            _store.DidNotReceive().Save(Arg.Any<AppSettings>());
            raised.ShouldBe(0);
            service.Current.ShouldBe(AppSettings.Defaults);
        }
    }
}
