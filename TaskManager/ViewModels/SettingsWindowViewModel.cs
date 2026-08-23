using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Models.Mappers;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Views;
using TaskManager.Utility.Utility;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="SettingsWindow"/>
    /// </summary>
    internal class SettingsWindowViewModel : ViewModelBase
    {
        public EditableSettings EditableSettings { get; }
        public IList<string> ProcessesRefreshFrequencyTypes { get; } =
            RefreshFrequencyTypeHelper.GetAllLocalized().ToArray();
        public IList<string> DateTimeFormats { get; } = [
            "yyyy_MM_dd--HH_mm_ss",
            "dd_MM_yyyy--HH_mm_ss",
            "MM_dd_yyyy--HH_mm_ss"];

        public ICommand SaveSettingsCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        private readonly ISettingsService _settingsService;
        private readonly IErrorHandler _errorHandler;

        public SettingsWindowViewModel(ISettingsService settingsService, IErrorHandler errorHandler)
        {
            _settingsService = settingsService;
            _errorHandler = errorHandler;

            EditableSettings = settingsService.ToEditables();

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            RestoreDefaultsCommand = new RelayCommand(_settingsService.RestoreDefaults);
        }

        public void SaveSettings()
        {
            _errorHandler.Guard(() => _settingsService.SaveSettings(EditableSettings), "saving settings");
        }
    }
}
