using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.Services.ErrorHandling;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for MainWindow. Headless-testable: depends only on interfaces; all window
    /// orchestration goes through IWindowService; startup happens via InitializeCommand
    /// invoked once by the composition root.
    /// </summary>
    internal class MainWindowViewModel : ObservableObject
    {
        private readonly IMessageService _messageService;
        private readonly IProcessListCatalog _catalog;
        private readonly IErrorHandler _errorHandler;
        private readonly ISettingsService _settings;
        private readonly IWindowService _windows;

        public MainWindowViewModel(IMessageService messageService, IProcessListCatalog catalog,
            IErrorHandler errorHandler, ISettingsService settings, IWindowService windows)
        {
            _messageService = messageService;
            _catalog = catalog;
            _errorHandler = errorHandler;
            _settings = settings;
            _windows = windows;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new AsyncRelayCommand(TerminateAsync);
            SetPriorityCommand = new AsyncRelayCommand(SetPriorityAsync);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.PerformRefreshAsync(isUserInitiated: true), "refreshing process list"));
            InitializeCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.InitializeAsync(), "loading initial process list"));

            // count forwarder: ProcessCount is owned by the catalog
            _catalog.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IProcessListCatalog.ProcessCount))
                {
                    OnPropertyChanged(nameof(ProcessCount));
                }
            };
        }

        #region Bindings
        public int ProcessCount => _catalog.ProcessCount;
        public IList<DataType> DataTypes => Enum.GetValues<DataType>();
        public ReadOnlyObservableCollection<ProcessItem> Processes => _catalog.Items;
        #endregion

        #region PureUI_Bindings
        public int SelectedTabIndex { get; set => SetProperty(ref field, value); }
        #endregion

        #region Commands
        public ICommand ExportCommand { get; }
        public AsyncRelayCommand TerminateCommand { get; }
        public AsyncRelayCommand SetPriorityCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand RefreshCommand { get; }
        public AsyncRelayCommand InitializeCommand { get; }
        #endregion

        private void OpenSettings()
        {
            // resx/x:Static localization is baked at compile time, so a language
            // switch still requires a restart; the decision lives here (composition flow).
            var languageBefore = _settings.Current.Language;

            _windows.ShowSettings();

            if (_settings.Current.Language != languageBefore)
            {
                App.Restart();
            }
        }

        private void Export() => _windows.ShowExport(_catalog.SnapshotForExport());

        private IEnumerable<ProcessItem> GetSelectedProcesses() => Processes.Where(p => p.IsSelected);

        private int[] GetSelectedPids() =>
            GetSelectedProcesses().Select(x => x.Process.Pid).ToArray();

        private async Task TerminateAsync()
        {
            if (!EnsureSelection())
            {
                return;
            }

            if (_messageService.ShowMessage(Strings.AskingForConfirmation, Strings.Confirm,
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
            {
                return;
            }

            await _errorHandler.GuardAsync(async () =>
            {
                var summary = await _catalog.TerminateProcessesAsync(GetSelectedPids());
                ReportPartialFailures(summary);
            }, "terminating selected processes");
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            _messageService.ShowMessage(OperationSummaryReporter.FormatPartialFailures(summary),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async Task SetPriorityAsync()
        {
            if (!EnsureSelection())
            {
                return;
            }

            await _errorHandler.GuardAsync(() =>
            {
                _windows.ShowSetPriority(GetSelectedPids());
                return Task.CompletedTask;
            }, "opening the set-priority dialog");
        }

        private bool EnsureSelection()
        {
            if (Processes.Any(x => x.IsSelected))
            {
                return true;
            }

            _messageService.ShowMessage(Strings.SelectProcess, Strings.Error,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
