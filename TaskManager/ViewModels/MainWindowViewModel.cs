using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Collections;
using System.Text;
using System.Windows.Threading;
using System.Net.Sockets;
using System.Reflection;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Task_Manager.Services.Data_Export;
using Task_Manager.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Domain.Services;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;
using TaskManager.Shared.Resources.Languages;
using Task_Manager.Services.Factories;

namespace Task_Manager.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        #region ALL_FIELDS
        // services
        private readonly IServiceProvider _serviceProvider;
        private readonly ProcessManager _processManager;

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
        private ObservableCollection<ProcessItem> _processes;
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
        private void ProcessManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
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
        private ImageSource _monitoringButtonIcon;
        public ImageSource MonitoringButtonIcon
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
        public ICommand BetterDataGrid_SelectionChangedCommand { get; private set; }
        public ICommand ExportCommand { get; private set; }
        public ICommand TerminateCommand { get; private set; }
        public ICommand SetPriorityCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        #endregion
        #endregion

        public MainWindowViewModel(IServiceProvider serviceProvider, ProcessManager processManager)
        {
            _serviceProvider = serviceProvider;
            _processManager = processManager;

            BindFunctionsToCommands();

            // load running processes asynchronously
            _processManager.LoadProcesses();
			_processManager.StartPollingProcesses();

			// initialize visuals
			//UpdateIcon(MonitoringIcon);

            // binding synchronizers initialization
            _processManager.PropertyChanged += ProcessManager_PropertyChanged;

            // late add events
            _processManager.Processes.CollectionChanged += _processManager.Processes_CollectionChanged;

            App.App_Close += App_Close;
        }

        private void BindFunctionsToCommands()
        {
            BetterDataGrid_SelectionChangedCommand = new RelayCommand<SelectionChangedEventArgs>(BetterDataGrid_OnSelectionChanged);
            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new RelayCommand(TerminateProcesses);
            SetPriorityCommand = new RelayCommand(SetPriority);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand<object>(_processManager.PerformRefresh);
        }

        private void OpenSettings()
        {
            SettingsWindow settingsWindow = new SettingsWindow();
            settingsWindow.DataContext = new SettingsWindowViewModel();

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
            return _processManager.GetProcessList().Where(p => p.IsSelected).ToList();
        }

        private void TerminateProcesses()
        {
            if (!ValidatePreconditions(Preconditions.SelectedAnyProcess))
            {
                return;
            }

            List<System.Diagnostics.Process> processes = new List<System.Diagnostics.Process>();
            foreach (var process in GetSelectedProcesses())
            {
                processes.Add(System.Diagnostics.Process.GetProcessById(Convert.ToInt32(process.Process.Pid)));
            }

            if (MessageBox.Show(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
            {
                return;
            }

            _processManager.TerminateProcesses(processes);
        }

        private void SetPriority()
        {
            if (!ValidatePreconditions(Preconditions.SelectedAnyProcess))
            {
                return;
            }

            List<System.Diagnostics.Process> processes = new List<System.Diagnostics.Process>();
            foreach (var process in GetSelectedProcesses())
            {
                processes.Add(System.Diagnostics.Process.GetProcessById(Convert.ToInt32(process.Process.Pid)));
            }

            SetPriorityWindow setPriorityWindow = new SetPriorityWindow();
            var setPriorityWindowVM = new SetPriorityWindowViewModel();
            setPriorityWindowVM.Processes = processes;
            setPriorityWindow.DataContext = setPriorityWindowVM;
            setPriorityWindowVM.ProcessManager = _processManager;

            setPriorityWindow.ShowDialog();
        }

		private void BetterDataGrid_OnSelectionChanged(SelectionChangedEventArgs e)
		{
			// update model selection state based on newly selected rows
			foreach (ProcessItem selectedItem in e.AddedItems)
			{
				selectedItem.IsSelected = true;
			}
			int threadCount = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
			System.Diagnostics.Debug.WriteLine($"Thread Count: {threadCount}");

			ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
			System.Diagnostics.Debug.WriteLine($"Worker Threads Available: {workerThreads}");
			System.Diagnostics.Debug.WriteLine($"Completion Port Threads Available: {completionPortThreads}");
			// update model selection state based on newly DEselected rows
			foreach (ProcessItem unselectedItem in e.RemovedItems)
			{
				unselectedItem.IsSelected = false;
			}
		}

        private bool ValidatePreconditions(Preconditions preconditions)
        {
			if ((preconditions & Preconditions.SelectedAnyProcess) == Preconditions.SelectedAnyProcess)
			{
				if (Processes == null || _processManager.GetProcessList().Count(x => x.IsSelected) == 0)
				{
					MessageBox.Show(Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void UpdateIcon(string path)
        {
            // load image dynamically
        }

		private void MoveTabPanelToLogsTab()
        {
            const int logsTabIndex = 1;
            SelectedTabIndex = logsTabIndex;
        }

        private async void App_Close(object sender)
        {

        }
    }
}
