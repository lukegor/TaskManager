using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services.Factories;
using TaskManager.Services.ErrorHandling;
using TaskManager.Shared.Resources.Languages;
using TaskManager.UI.Views;
using TaskManager.Utility.Utility;
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
        private readonly IDispatcherService _dispatcherService;
        private readonly ProcessManager _processManager;
        private readonly IErrorHandler _errorHandler;

        // icon paths
        // ...

        #region Bindings
        #region Binding Getters / Exposers
        public int ProcessCount => _processManager.ProcessCount;
        public IList<DataType> DataTypes => Enum.GetValues<DataType>();
        #endregion

        #region OneWay_Bingings
        //private Permissions selectedPermissions = new Permissions(false);
        //public Permissions SelectedPermissions
        //{
        //    get { return selectedPermissions; }
        //    set
        //    {
        //        SetProperty(ref selectedPermissions, value);
        //    }
        //}
        #endregion
        #region TwoWay_Bindings
        private ObservableCollection<ProcessItem> _processes = new();
		public ObservableCollection<ProcessItem> Processes
		{
			get => _processes;
			set
			{
				if (SetProperty(ref _processes, value))
				{
					// sync with ProcessManager when MainWindowViewModel.Processes changes
					_processManager.Processes = value;
				}
			}
		}
        #endregion

        #region Binding_Synchronizers
        private void ProcessManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // notify if value under ProcessCount getter changed
            // because UI won't react to SetProperty in ProcessManager.ProcessCount notification
            if (e.PropertyName == nameof(ProcessManager.ProcessCount))
            {
                OnPropertyChanged(nameof(ProcessCount));
            }

            if (e.PropertyName == nameof(ProcessManager.Processes))
            {
				Processes = _processManager.Processes;
			}
		}
        #endregion

        #region PureUI_Bindings
        private ImageSource? _monitoringButtonIcon;
        public ImageSource? MonitoringButtonIcon
        {
            get => _monitoringButtonIcon;
            set
            {
                SetProperty(ref _monitoringButtonIcon, value);
            }
        }

        private int selectedTabIndex;
        public int SelectedTabIndex
        {
            get => selectedTabIndex;
            set
            {
                SetProperty(ref selectedTabIndex, value);
            }
        }
        #endregion
        #endregion

		#region Commands
        public ICommand ExportCommand { get; private set; }
        public ICommand TerminateCommand { get; private set; }
        public ICommand SetPriorityCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        #endregion
        #endregion

        public MainWindowViewModel(IServiceProvider serviceProvider,
            IMessageService messageService,
            IDispatcherService dispatcherService,
            ProcessManager processManager,
            IErrorHandler errorHandler)
        {
            _serviceProvider = serviceProvider;
            _messageService = messageService;
            _dispatcherService = dispatcherService;
            _processManager = processManager;
            _errorHandler = errorHandler;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new RelayCommand(TerminateProcesses);
            SetPriorityCommand = new RelayCommand(SetPriority);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand(() => _processManager.PerformRefresh(isUserInitiated: true));

            // load running processes synchronously; a startup failure must not abort the app
            _errorHandler.Guard(() => _processManager.LoadProcesses().GetAwaiter().GetResult(),
                "loading initial process list");
			_processManager.StartPollingProcesses();

            // binding synchronizers initialization
            _processManager.PropertyChanged += ProcessManager_PropertyChanged;

            // late add events
            _processManager.Processes.CollectionChanged += _processManager.Processes_CollectionChanged;
        }

        private void OpenSettings()
        {
            SettingsWindow settingsWindow = new SettingsWindow();
            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            settingsWindow.DataContext = new SettingsWindowViewModel(settingsService, _errorHandler);

            settingsWindow.ShowDialog();
        }

		private void Export()
		{
            DataExportWindow exportWindow = _serviceProvider.GetRequiredService<DataExportWindow>();
            var processes = _processManager.Processes.Select(x => x.Process);
            exportWindow.DataContext = _serviceProvider.GetRequiredService<DataExportViewModelFactory>().Create(processes);

            exportWindow.ShowDialog();
		}

        private IEnumerable<ProcessItem> GetSelectedProcesses()
        {
            return _processManager.Processes.Where(p => p.IsSelected);
        }

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
                var summary = _processManager.TerminateProcesses(GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)));
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
            SetPriorityWindow setPriorityWindow = factory.Create(GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)));

            setPriorityWindow.ShowDialog();

            _dispatcherService.Invoke(() =>
            {
                var view = CollectionViewSource.GetDefaultView(Processes);
                view?.Refresh();
            });
        }

        private bool ValidatePreconditions(Preconditions preconditions)
        {
			if ((preconditions & Preconditions.SelectedAnyProcess) == Preconditions.SelectedAnyProcess)
			{
				if (Processes == null || !_processManager.Processes.Any(x => x.IsSelected))
				{
					_messageService.ShowMessage(Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
					return false;
				}
			}

            //if ((preconditions & Preconditions.GotConfirmation) == Preconditions.GotConfirmation)
            //{
            //    if (AskForConfirmation("block") == MessageBoxResult.No)
            //    {
            //        return false;
            //    }
            //}

            return true;
        }
    }
}
