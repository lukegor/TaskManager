using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Models
{
    public class EditableSettings : ObservableObject
    {
        private string _language = string.Empty;
        public string Language
        {
            get => _language;
            set => SetProperty(ref _language, value);
        }

        private RefreshFrequencyType _processesRefreshFrequency;
        public RefreshFrequencyType ProcessesRefreshFrequency
        {
            get => _processesRefreshFrequency;
            set => SetProperty(ref _processesRefreshFrequency, value);
        }

        private string _dateTimeFormat = string.Empty;
        public string DateTimeFormat
        {
            get => _dateTimeFormat;
            set => SetProperty(ref _dateTimeFormat, value);
        }

        public EditableSettings()
        {

        }
    }
}
