using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.Services.ErrorHandling;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for DataExportWindow. Exports a fixed, materialized process snapshot via
    /// the composition-root-supplied exporter factory; modeled failures are reported as data.
    /// </summary>
    internal class DataExportWindowViewModel : ObservableObject, IRequestCloseObservable
    {
        public ExportationType? Exportation { get; set => SetProperty(ref field, value); }

        public DataType? DataType { get; set => SetProperty(ref field, value); }

        public string DirPath { get; set => SetProperty(ref field, value); } = string.Empty;

        public IList<ExportationType> Exportations { get; } = Enum.GetValues<ExportationType>();
        public IList<DataType> Extensions { get; } = Enum.GetValues<DataType>();

        public ICommand SelectFolderCommand { get; }
        public ICommand OnConfirmClick { get; }

        public event EventHandler? RequestClose;

        public bool Confirmed { get; private set; }

        private readonly IReadOnlyList<Process> _processes;
        private readonly Func<DataType, BaseDataExporter> _exporterFactory;
        private readonly IFolderPicker _folderPicker;
        private readonly IMessageService _messageService;
        private readonly IErrorHandler _errorHandler;

        public DataExportWindowViewModel(IMessageService messageService, IErrorHandler errorHandler,
            Func<DataType, BaseDataExporter> exporterFactory, IFolderPicker folderPicker,
            IReadOnlyList<Process> processes)
        {
            _messageService = messageService;
            _errorHandler = errorHandler;
            _exporterFactory = exporterFactory;
            _folderPicker = folderPicker;
            _processes = processes.ToArray(); // hold a materialized copy; caller may mutate afterwards

            SelectFolderCommand = new RelayCommand(SelectFolder);
            OnConfirmClick = new RelayCommand(OnConfirm);
        }

        private void SelectFolder() => DirPath = _folderPicker.PickFolder() ?? string.Empty;

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
                {
                    _messageService.ShowMessage("You need to select options", Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!TryExport(dataType))
                {
                    return; // failure already reported; keep the window open for a corrected attempt
                }

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "exporting process data");
        }

        internal bool TryExport(DataType dataType)
        {
            return _errorHandler.Guard(() =>
            {
                var result = _exporterFactory(dataType).Export(DirPath, _processes);
                if (result.IsSuccess)
                {
                    return true;
                }

                _messageService.ShowMessage(
                    string.Format(Strings.ExportFailedFormat, DirPath) + " " + DescribeFailure(result.FailureReason!.Value),
                    Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }, "exporting process data");
        }

        private static string DescribeFailure(ExportFailureReason reason) => reason switch
        {
            ExportFailureReason.AccessDenied => Strings.ExportFailedAccessDenied,
            ExportFailureReason.InvalidPath => Strings.ExportFailedInvalidPath,
            _ => Strings.ExportFailedIo
        };
    }
}
