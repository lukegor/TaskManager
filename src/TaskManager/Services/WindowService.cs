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

        public void ShowSettings()
        {
            var window = new SettingsWindow
            {
                DataContext = new SettingsWindowViewModel(_settings, _errorHandler)
            };
            window.ShowDialog();
        }

        public void ShowExport(IReadOnlyList<Process> processes)
        {
            var vm = new DataExportWindowViewModel(_messages, _errorHandler, _exporterFactory, _folderPicker, processes);
            ShowDialogWithCloseRelay(new DataExportWindow(), vm);
        }

        public bool ShowSetPriority(IReadOnlyCollection<int> pids)
        {
            var vm = new SetPriorityWindowViewModel(_messages, _catalog, pids, _errorHandler);
            ShowDialogWithCloseRelay(new SetPriorityWindow(), vm);
            return vm.Confirmed;
        }

        private static void ShowDialogWithCloseRelay(System.Windows.Window window, ObservableObject vm)
        {
            window.DataContext = vm;
            if (vm is IRequestCloseObservable closable)
            {
                closable.RequestClose += (_, _) => window.Close();
            }

            window.ShowDialog();
        }
    }
}
