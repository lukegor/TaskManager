using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Services
{
    /// <summary>
    /// Composition-root window orchestration: builds windows + their ViewModels, owns dialog
    /// flow and close relaying. This is the ONLY place that constructs windows.
    /// </summary>
    internal sealed class WindowService : IWindowService
    {
        private readonly ISettingsService _settings;
        private readonly IErrorHandler _errorHandler;
        private readonly IMessageService _messages;
        private readonly IProcessListCatalog _catalog;
        private readonly Func<DataType, BaseDataExporter> _exporterFactory;
        private readonly IFolderPicker _folderPicker;

        public WindowService(ISettingsService settings, IErrorHandler errorHandler, IMessageService messages,
            IProcessListCatalog catalog, Func<DataType, BaseDataExporter> exporterFactory, IFolderPicker folderPicker)
        {
            _settings = settings;
            _errorHandler = errorHandler;
            _messages = messages;
            _catalog = catalog;
            _exporterFactory = exporterFactory;
            _folderPicker = folderPicker;
        }

        public void ShowSettings() => CreateSettingsDialog().Window.ShowDialog();

        public void ShowAbout() => CreateAboutDialog().Window.ShowDialog();

        public void ShowExport(IReadOnlyList<Process> processes) =>
            CreateExportDialog(processes).Window.ShowDialog();

        public bool ShowSetPriority(IReadOnlyCollection<int> pids)
        {
            var dialog = CreatePriorityDialog(pids);
            dialog.Window.ShowDialog();
            return dialog.ViewModel.Confirmed;
        }

        internal (AboutWindow Window, AboutWindowViewModel ViewModel) CreateAboutDialog()
        {
            var viewModel = new AboutWindowViewModel();
            var window = new AboutWindow { DataContext = viewModel };
            return (window, viewModel);
        }

        internal (SettingsWindow Window, SettingsWindowViewModel ViewModel) CreateSettingsDialog()
        {
            var viewModel = new SettingsWindowViewModel(_settings, _errorHandler);
            var window = new SettingsWindow { DataContext = viewModel };
            return (window, viewModel);
        }

        internal (DataExportWindow Window, DataExportWindowViewModel ViewModel) CreateExportDialog(
            IReadOnlyList<Process> processes)
        {
            var viewModel = new DataExportWindowViewModel(
                _messages, _errorHandler, _exporterFactory, _folderPicker, processes);
            var window = new DataExportWindow { DataContext = viewModel };
            AttachCloseRelay(window, viewModel);
            return (window, viewModel);
        }

        internal (SetPriorityWindow Window, SetPriorityWindowViewModel ViewModel) CreatePriorityDialog(
            IReadOnlyCollection<int> pids)
        {
            var viewModel = new SetPriorityWindowViewModel(_messages, _catalog, pids, _errorHandler);
            var window = new SetPriorityWindow { DataContext = viewModel };
            AttachCloseRelay(window, viewModel);
            return (window, viewModel);
        }

        private static void AttachCloseRelay(System.Windows.Window window, object viewModel)
        {
            // only dialog VMs that own their close decision raise RequestClose
            // (e.g. the settings window is closed by its own Save/Cancel buttons)
            if (viewModel is IRequestCloseObservable closable)
            {
                closable.RequestClose += (_, _) => window.Close();
            }
        }
    }
}
