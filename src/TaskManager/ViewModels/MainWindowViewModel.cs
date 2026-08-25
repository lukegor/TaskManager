using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Presentation;
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
        private readonly IElevationService _elevation;

        public MainWindowViewModel(IMessageService messageService, IProcessListCatalog catalog,
            IErrorHandler errorHandler, ISettingsService settings, IWindowService windows,
            IElevationService elevationService)
        {
            _messageService = messageService;
            _catalog = catalog;
            _errorHandler = errorHandler;
            _settings = settings;
            _windows = windows;
            _elevation = elevationService;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new AsyncRelayCommand(TerminateAsync);
            SetPriorityCommand = new AsyncRelayCommand(SetPriorityAsync);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            OpenAboutCommand = new RelayCommand(() => _windows.ShowAbout());
            RefreshCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.PerformRefreshAsync(isUserInitiated: true), "refreshing process list"));
            InitializeCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _catalog.InitializeAsync(), "loading initial process list"));
            RelaunchElevatedCommand = new RelayCommand(RelaunchElevated);

            // forwarders: count/status/diagnostics are owned by the catalog
            _catalog.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IProcessListCatalog.ProcessCount))
                {
                    OnPropertyChanged(nameof(ProcessCount));
                }

                if (e.PropertyName == nameof(IProcessListCatalog.IsPollingPaused))
                {
                    OnPropertyChanged(nameof(IsPollingPaused));
                }

                if (e.PropertyName == nameof(IProcessListCatalog.LastRefresh))
                {
                    OnPropertyChanged(nameof(LastRefresh));
                    OnPropertyChanged(nameof(OutcomeText));
                    OnPropertyChanged(nameof(OutcomeKind));
                }
            };

            // interval text is derived from settings only
            _settings.Changed += _ => OnPropertyChanged(nameof(IntervalText));
        }

        #region Bindings
        public int ProcessCount => _catalog.ProcessCount;
        public RefreshDiagnostics? LastRefresh => _catalog.LastRefresh;
        public bool IsPollingPaused => _catalog.IsPollingPaused;
        public string IntervalText
        {
            get
            {
                var seconds = RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency];
                return seconds == 0
                    ? Strings.StatusPaused
                    : string.Format(CultureInfo.CurrentCulture, Strings.StatusEverySeconds, seconds);
            }
        }

        public string OutcomeText => _catalog.LastRefresh?.Outcome switch
        {
            RefreshOutcome.Ok => Strings.StatusOutcomeOk,
            RefreshOutcome.Skipped => Strings.StatusOutcomeSkipped,
            RefreshOutcome.Failed => Strings.StatusOutcomeFailed,
            _ => "-",
        };

        public RefreshOutcome? OutcomeKind => _catalog.LastRefresh?.Outcome;

        public string ElevationText => _elevation.IsAdministrator
            ? Strings.StatusAdministrator
            : Strings.StatusStandard;

        public bool CanRelaunchElevated => !_elevation.IsAdministrator;

        public static IList<DataType> DataTypes => Enum.GetValues<DataType>();
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
        public ICommand OpenAboutCommand { get; }
        public ICommand RefreshCommand { get; }
        public AsyncRelayCommand InitializeCommand { get; }
        public ICommand RelaunchElevatedCommand { get; }
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

        private void RelaunchElevated() =>
            _errorHandler.Guard(App.RelaunchElevated, "relaunching as administrator");

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
