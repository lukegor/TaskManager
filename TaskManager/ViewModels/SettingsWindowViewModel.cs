using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Task_Manager.Properties;
using Task_Manager.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TaskManager.Utility.Utility;

namespace Task_Manager.ViewModels
{
    internal class SettingsWindowViewModel : ObservableObject
    {
        private string language;
        public string Language
        {
            get { return language; }
            set { if (SetProperty(ref language, value)) { _settingsProcessor.Language = value; } }
        }

        private RefreshFrequencyType processesRefreshFrequency;
        public RefreshFrequencyType ProcessesRefreshFrequency
        {
            get { return processesRefreshFrequency; }
            set { if (SetProperty(ref processesRefreshFrequency, value)) { _settingsProcessor.ProcessesRefreshFrequency = value; } }
        }

        private string dateTimeFormat;
        public string DateTimeFormat
        {
            get { return dateTimeFormat; }
            set { if (SetProperty(ref dateTimeFormat, value)) { _settingsProcessor.DateTimeFormat = value; } }
        }

        // This event handler is called when SettingsProcessor raises PropertyChanged.
        private void SettingsProcessor_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsProcessor.Language))
            {
                Language = _settingsProcessor.Language;
            }
            else if (e.PropertyName == nameof(SettingsProcessor.ProcessesRefreshFrequency))
            {
                ProcessesRefreshFrequency = _settingsProcessor.ProcessesRefreshFrequency;
            }
            else if (e.PropertyName == nameof(SettingsProcessor.DateTimeFormat))
            {
                DateTimeFormat = _settingsProcessor.DateTimeFormat;
            }
        }

        public IList<string> ProcessesRefreshFrequencyTypes { get; } =
            RefreshFrequencyTypeHelper.GetAllLocalized().ToList();
        public IList<string> DateTimeFormats { get; } = [
            "yyyy_MM_dd--HH_mm_ss",
            "dd_MM_yyyy--HH_mm_ss",
            "MM_dd_yyyy--HH_mm_ss"];

        public ICommand SaveSettingsCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        private readonly SettingsProcessor _settingsProcessor;

        public SettingsWindowViewModel()
        {
            _settingsProcessor = new SettingsProcessor();
            _settingsProcessor.PropertyChanged += SettingsProcessor_PropertyChanged;
            _settingsProcessor.ProcessPropertyValues();

            SaveSettingsCommand = new RelayCommand(_settingsProcessor.SaveSettings);
            RestoreDefaultsCommand = new RelayCommand(_settingsProcessor.RestoreDefaults);
        }
    }
}
