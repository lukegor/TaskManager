using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TaskManager.Utility.Utility;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.Services
{
    public class SettingsProcessor : ObservableObject
    {
        private string language;
        public string Language
        {
            get { return language; }
            set { SetProperty(ref language, value); }
        }

        private RefreshFrequencyType processesRefreshFrequency;
        public RefreshFrequencyType ProcessesRefreshFrequency
        {
            get { return processesRefreshFrequency; }
            set { SetProperty(ref processesRefreshFrequency, value); }
        }

        private string dateTimeFormat;
        public string DateTimeFormat
        {
            get { return dateTimeFormat; }
            set { SetProperty(ref dateTimeFormat, value); }
        }

        public void ProcessPropertyValues()
        {
            try
            {
                GetCurrentPropertyValues();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                MessageBox.Show($"Loading personalized settings failed.{Environment.NewLine}{Environment.NewLine}Start loading default settings...",
                    Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);

                GetDefaultPropertyValues();
            }
        }

        private void GetCurrentPropertyValues()
        {
            Language = string.IsNullOrEmpty(Settings.Default.LanguageVersion)
                    ? "English"
                    : Settings.Default.LanguageVersion;
            ProcessesRefreshFrequency = (RefreshFrequencyType)int.Parse(Settings.Default.RefreshRate);
            DateTimeFormat = Settings.Default.TimeStampFormat;
        }

        private void GetDefaultPropertyValues()
        {
            Language = "English";
            ProcessesRefreshFrequency = (RefreshFrequencyType)Convert.ToInt32(Settings.Default.Properties["RefreshRate"].DefaultValue);
            DateTimeFormat = Settings.Default.Properties["TimeStampFormat"].DefaultValue.ToString()!;
        }

        public void SaveSettings()
        {
            string tmp = Settings.Default.LanguageVersion;
            Settings.Default.LanguageVersion = Language;

            Settings.Default.RefreshRate = ((int)ProcessesRefreshFrequency).ToString();

            Settings.Default.TimeStampFormat = DateTimeFormat;

            Settings.Default.Save();

            if (tmp != Language)
            {
                App current = (App)App.Current;
                current.Restart();
            }
        }

        public void RestoreDefaults()
        {
            Settings.Default.Reset();
        }
    }
}
