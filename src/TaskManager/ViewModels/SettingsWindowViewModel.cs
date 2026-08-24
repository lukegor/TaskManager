using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
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
        private readonly ISettingsService _settingsService;
        private readonly IErrorHandler _errorHandler;

        public IList<string> Languages { get; } = [.. LanguageDictionary.KeysList];
        public IList<string> ProcessesRefreshFrequencyTypes { get; } =
            RefreshFrequencyTypeHelper.GetAllLocalized().ToArray();
        public IList<string> DateTimeFormats { get; } = [.. AppSettings.AllowedDateTimeFormats];

        public string Language { get; set => SetProperty(ref field, value); } = string.Empty;

        public RefreshFrequencyType ProcessesRefreshFrequency { get; set => SetProperty(ref field, value); }

        public string DateTimeFormat { get; set => SetProperty(ref field, value); } = string.Empty;

        public ICommand SaveSettingsCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        public SettingsWindowViewModel(ISettingsService settingsService, IErrorHandler errorHandler)
        {
            _settingsService = settingsService;
            _errorHandler = errorHandler;

            Apply(settingsService.Current);

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        }

        public void SaveSettings()
        {
            _errorHandler.Guard(() => _settingsService.Update(new AppSettings
            {
                Language = Language,
                ProcessesRefreshFrequency = ProcessesRefreshFrequency,
                DateTimeFormat = DateTimeFormat
            }), "saving settings");
        }

        public void RestoreDefaults()
        {
            _errorHandler.Guard(() =>
            {
                Apply(AppSettings.Defaults);
                _settingsService.Update(AppSettings.Defaults);
            }, "restoring default settings");
        }

        private void Apply(AppSettings settings)
        {
            Language = settings.Language;
            ProcessesRefreshFrequency = settings.ProcessesRefreshFrequency;
            DateTimeFormat = settings.DateTimeFormat;
        }
    }
}
