using CommunityToolkit.Mvvm.ComponentModel;
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
        private string _language;
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

        private string _dateTimeFormat;
        public string DateTimeFormat
        {
            get { return _dateTimeFormat; }
            set { SetProperty(ref _dateTimeFormat, value); }
        }

        private readonly IMessageService _messageService;

        public SettingsService(IMessageService messageService)
        {
            _messageService = messageService;

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
                System.Diagnostics.Debug.WriteLine(ex);

                _messageService.ShowMessage(Strings.LoadingSettingsFailed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);

                LoadDefaultSettings();
            }
        }

        private void LoadSettings()
        {
            Language = Settings.Default.LanguageVersion;
            RefreshFrequency = (RefreshFrequencyType)int.Parse(Settings.Default.RefreshFrequency);
            DateTimeFormat = Settings.Default.DateTimeFormat;
        }

        private void LoadDefaultSettings()
        {
            Language = (string)GetDefaultSettingValue(nameof(Settings.Default.LanguageVersion));
            RefreshFrequency = (RefreshFrequencyType)Convert.ToInt32(nameof(Settings.Default.RefreshFrequency));
            DateTimeFormat = (string)GetDefaultSettingValue(nameof(Settings.Default.DateTimeFormat));
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
