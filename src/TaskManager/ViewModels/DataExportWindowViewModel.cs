using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
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
        public AsyncRelayCommand OnConfirmClick { get; }

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
            OnConfirmClick = new AsyncRelayCommand(OnConfirmAsync);
        }

        private void SelectFolder() => DirPath = _folderPicker.PickFolder() ?? string.Empty;

        private async Task OnConfirmAsync()
        {
            await _errorHandler.GuardAsync(async () =>
            {
                if (Exportation is not ExportationType || DataType is not DataType dataType)
                {
                    _messageService.ShowMessage(Strings.SelectOptionsRequired, Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!await TryExportAsync(dataType))
                {
                    return; // failure already reported; keep the window open for a corrected attempt
                }

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "exporting process data");
        }

        internal async Task<bool> TryExportAsync(DataType dataType)
        {
            return await _errorHandler.GuardAsync(async () =>
            {
                // exporters are stateless per call; ClosedXML workbooks can take seconds,
                // so the file generation runs off the UI thread
                var result = await Task.Run(() => _exporterFactory(dataType).Export(DirPath, _processes));
                if (result.IsSuccess)
                {
                    return true;
                }

                _messageService.ShowMessage(
#pragma warning disable CA1863 // format string is culture-resolved localization (Strings.*); caching a CompositeFormat would freeze one UI language
                    string.Format(CultureInfo.CurrentCulture, Strings.ExportFailedFormat, DirPath) + " " + DescribeFailure(result.FailureReason!.Value),
#pragma warning restore CA1863
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
