using Microsoft.Extensions.Logging;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Properties;
using TaskManager.Shared.Resources.Languages;
using TaskManager.Utility.Utility;

namespace TaskManager.Services
{
    /// <summary>Interim adapter over the legacy ApplicationSettingsBase store;
    /// replaced by the JSON-backed implementation in this phase.</summary>
    internal class SettingsService : ISettingsService
    {
        private readonly IMessageService _messageService;
        private readonly ILogger<SettingsService> _logger;

        public AppSettings Current { get; private set; } = AppSettings.Defaults;

        public event Action<AppSettings>? Changed;

        public SettingsService(IMessageService messageService, ILogger<SettingsService> logger)
        {
            _messageService = messageService;
            _logger = logger;

            ProcessPropertyValues();
        }

        private void ProcessPropertyValues()
        {
            try
            {
                Current = LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading personalized settings failed; falling back to defaults");
                _messageService.ShowMessage(Strings.LoadingSettingsFailed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected virtual AppSettings LoadSettings()
        {
            return new AppSettings
            {
                Language = Settings.Default.LanguageVersion,
                ProcessesRefreshFrequency = (RefreshFrequencyType)int.Parse(Settings.Default.RefreshFrequency),
                DateTimeFormat = Settings.Default.DateTimeFormat
            };
        }

        public void Update(AppSettings settings)
        {
            var isChangedLanguage = Settings.Default.LanguageVersion != settings.Language;

            Settings.Default.LanguageVersion = settings.Language;
            Settings.Default.RefreshFrequency = ((int)settings.ProcessesRefreshFrequency).ToString();
            Settings.Default.DateTimeFormat = settings.DateTimeFormat;
            Settings.Default.Save();

            Current = settings;
            Changed?.Invoke(settings);

            if (isChangedLanguage)
            {
                App.Restart();
            }
        }
    }
}
