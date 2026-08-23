using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Models
{
    public class EditableSettings : ObservableObject
    {
        public string Language { get; set => SetProperty(ref field, value); } = string.Empty;

        public RefreshFrequencyType ProcessesRefreshFrequency { get; set => SetProperty(ref field, value); }

        public string DateTimeFormat { get; set => SetProperty(ref field, value); } = string.Empty;

        public EditableSettings()
        {

        }
    }
}
