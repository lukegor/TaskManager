using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.Domain.Abstractions;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services.Factories;
using TaskManager.Services.ErrorHandling;
using TaskManager.Resources.Languages;
using TaskManager.UI.Views;
using TaskManager.Domain.Primitives;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="MainWindow"/>
    /// </summary>
    internal class MainWindowViewModel : ViewModelBase
    {
        #region ALL_FIELDS
        // services
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageService _messageService;
        private readonly ProcessManager _processManager;
        private readonly ProcessOperationsService _processOps;
        private readonly IErrorHandler _errorHandler;
        private readonly ISettingsService _settings;

        #region Bindings
        public int ProcessCount => _processManager.ProcessCount;
        public IList<DataType> DataTypes => Enum.GetValues<DataType>();

        public ReadOnlyObservableCollection<ProcessItem> Processes => _processManager.Items;
        #endregion

        #region PureUI_Bindings
        public ImageSource? MonitoringButtonIcon { get; set => SetProperty(ref field, value); }

        public int SelectedTabIndex { get; set => SetProperty(ref field, value); }
        #endregion
        #endregion

		#region Commands
        public ICommand ExportCommand { get; private set; }
        public ICommand TerminateCommand { get; private set; }
        public ICommand SetPriorityCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        #endregion

        public MainWindowViewModel(IServiceProvider serviceProvider,
            IMessageService messageService,
            ProcessManager processManager,
            ProcessOperationsService processOps,
            IErrorHandler errorHandler,
            ISettingsService settings)
        {
            _serviceProvider = serviceProvider;
            _messageService = messageService;
            _processManager = processManager;
            _processOps = processOps;
            _errorHandler = errorHandler;
            _settings = settings;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new RelayCommand(TerminateProcesses);
            SetPriorityCommand = new RelayCommand(SetPriority);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _processManager.PerformRefresh(isUserInitiated: true), "refreshing process list"));

            // load running processes in the background; the window must not block on enumeration
            _ = _errorHandler.GuardAsync(() => _processManager.LoadProcesses(), "loading initial process list");
            _processManager.StartPollingProcesses();

            // count forwarder: ProcessCount is owned by ProcessManager now
            _processManager.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessManager.ProcessCount))
                {
                    OnPropertyChanged(nameof(ProcessCount));
                }
            };
        }

		private void OpenSettings()
		{
            // resx/x:Static localization is baked at compile time, so a language
            // switch still requires a restart; the decision lives here (composition
            // flow), not in the settings service.
            var languageBefore = _settings.Current.Language;

            var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
            settingsWindow.ShowDialog();

            if (_settings.Current.Language != languageBefore)
            {
                App.Restart();
            }
		}

		private void Export()
		{
            DataExportWindow exportWindow = _serviceProvider.GetRequiredService<DataExportWindow>();
            var processes = _processManager.SnapshotForExport();
            exportWindow.DataContext = _serviceProvider.GetRequiredService<DataExportViewModelFactory>().Create(processes);

            exportWindow.ShowDialog();
		}

        private IEnumerable<ProcessItem> GetSelectedProcesses() => Processes.Where(p => p.IsSelected);

        private void TerminateProcesses()
        {
            if (!ValidatePreconditions(Preconditions.SelectedAnyProcess))
            {
                return;
            }
            if (_messageService.ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                == MessageBoxResult.Cancel)
            {
                return;
            }

            _errorHandler.Guard(() =>
            {
                var summary = _processOps.TerminateProcesses(
                    GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
                ReportPartialFailures(summary);
            });
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            var total = summary.SucceededPids.Count + summary.Failures.Count;
            _messageService.ShowMessage(
                string.Format(Strings.OpsCompletedWithFailuresFormat, summary.SucceededPids.Count, total),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SetPriority()
        {
            if (!ValidatePreconditions(Preconditions.SelectedAnyProcess))
            {
                return;
            }

            var factory = _serviceProvider.GetRequiredService<SetPriorityVVmFactory>();
            SetPriorityWindow setPriorityWindow = factory.Create(
                GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());

            setPriorityWindow.ShowDialog();
        }

        private bool ValidatePreconditions(Preconditions preconditions)
        {
			if ((preconditions & Preconditions.SelectedAnyProcess) == Preconditions.SelectedAnyProcess)
			{
				if (!Processes.Any(x => x.IsSelected))
				{
					_messageService.ShowMessage(Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
					return false;
				}
			}

            return true;
        }
    }
}
