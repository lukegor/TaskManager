using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Properties;
using TaskManager.Services;
using TaskManager.Utility.Utility;

namespace TaskManager.Tests
{
    /// <summary>
    /// Regression contract: corrupt personalized settings degrade to defaults.
    /// Neither the initial load nor the fallback may escape the constructor.
    /// </summary>
    public class SettingsServiceTests
    {
        private sealed class CorruptSettingsService : SettingsService
        {
            public CorruptSettingsService(IMessageService messageService)
                : base(messageService, NullLogger<SettingsService>.Instance)
            {
            }

            protected override void LoadSettings() =>
                throw new InvalidOperationException("simulated corrupt settings store");
        }

        [Fact]
        public void Constructor_WithCorruptSettings_FallsBackToDefaultsWithoutThrowing()
        {
            var messageService = Substitute.For<IMessageService>();
            var expectedLanguage = (string?)Settings.Default.Properties[nameof(Settings.Default.LanguageVersion)].DefaultValue;
            var expectedFrequencyText = (string?)Settings.Default.Properties[nameof(Settings.Default.RefreshFrequency)].DefaultValue;
            var expectedRefreshFrequency = (RefreshFrequencyType)int.Parse(expectedFrequencyText!);

            var service = new CorruptSettingsService(messageService);

            messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
            service.Language.ShouldBe(expectedLanguage);
            service.RefreshFrequency.ShouldBe(expectedRefreshFrequency);
        }
    }
}
