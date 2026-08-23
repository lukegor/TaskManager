using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Properties;
using TaskManager.Shared.Resources.Languages;
using TaskManager.Utility.Utility;

namespace TaskManager.Services
{
    internal class SettingsService : ObservableObject, IAppSettings, ISettingsService
    {
        private string _language = string.Empty;
        public string Language
        {
            get { return _language; }
            set { SetProperty(ref _language, value); }
        }

        private RefreshFrequencyType _refreshFrequency;
        public RefreshFrequencyType RefreshFrequency
        {
            get { return _refreshFrequency; }
            set { SetProperty(ref _refreshFrequency, value); }
        }

        private string _dateTimeFormat = string.Empty;
        public string DateTimeFormat
        {
            get { return _dateTimeFormat; }
            set { SetProperty(ref _dateTimeFormat, value); }
        }

        private readonly IMessageService _messageService;
        private readonly ILogger<SettingsService> _logger;

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
                LoadSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading personalized settings failed; falling back to defaults");
                _messageService.ShowMessage(Strings.LoadingSettingsFailed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                TryLoadDefaultSettings();
            }
        }

        protected virtual void LoadSettings()
        {
            Language = Settings.Default.LanguageVersion;
            RefreshFrequency = (RefreshFrequencyType)int.Parse(Settings.Default.RefreshFrequency);
            DateTimeFormat = Settings.Default.DateTimeFormat;
        }

        private void LoadDefaultSettings()
        {
            Language = (string?)GetDefaultSettingValue(nameof(Settings.Default.LanguageVersion)) ?? string.Empty;
            RefreshFrequency = (RefreshFrequencyType)int.Parse(
                (string?)GetDefaultSettingValue(nameof(Settings.Default.RefreshFrequency)) ?? ((int)RefreshFrequencyType.Low).ToString());
            DateTimeFormat = (string?)GetDefaultSettingValue(nameof(Settings.Default.DateTimeFormat)) ?? string.Empty;
        }

        private void TryLoadDefaultSettings()
        {
            try
            {
                LoadDefaultSettings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading default settings failed; keeping construction-time values");
            }
        }

        private object GetDefaultSettingValue(string propertyName)
        {
            return Settings.Default.Properties[propertyName].DefaultValue;
        }

        public void SaveSettings(EditableSettings newSettings)
        {
            var isChangedLanguage = Settings.Default.LanguageVersion != newSettings.Language;
            Settings.Default.LanguageVersion = newSettings.Language;

            Settings.Default.RefreshFrequency = ((int)newSettings.ProcessesRefreshFrequency).ToString();

            Settings.Default.DateTimeFormat = newSettings.DateTimeFormat;

            Settings.Default.Save();

            if (isChangedLanguage)
            {
                App.Restart();
            }
        }

        public void RestoreDefaults()
        {
            Settings.Default.Reset();
        }
    }
}
