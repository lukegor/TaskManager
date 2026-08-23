using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Utility;
using TaskManager.Services.Factories;
using TaskManager.Services.ErrorHandling;
using TaskManager.Shared.Resources.Languages;
using TaskManager.UI.Views;
using TaskManager.Utility.Utility;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="DataExportWindow"/>
    /// </summary>
    internal class DataExportWindowViewModel : ViewModelBase
	{
		#region All_Fields
		private readonly FolderSelector _folderSelector = new FolderSelector();

		public ExportationType? Exportation { get; set => SetProperty(ref field, value); }

		public DataType? DataType { get; set => SetProperty(ref field, value); }

		public string DirPath
		{
			get;
			set
			{
				if (SetProperty(ref field, value))
				{
					_folderSelector.DirPath = value;
				}
			}
		} = string.Empty;

		public IList<ExportationType> Exportations { get; } = Enum.GetValues<ExportationType>();
		public IList<DataType> Extensions { get; } = Enum.GetValues<DataType>();

		private void FolderSelector_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(FolderSelector.DirPath))
			{
				OnPropertyChanged(nameof(DirPath));
				this.DirPath = _folderSelector.DirPath;
			}
		}

		private IEnumerable<Process> processes;

		public ICommand SelectFolderCommand { get; }
		public ICommand OnConfirmClick { get; }
		#endregion

		private readonly IServiceProvider _serviceProvider;
        private readonly ISettingsService _settings;
		private readonly IMessageService _messageService;
		private readonly IErrorHandler _errorHandler;

		public DataExportWindowViewModel(IServiceProvider serviceProvider,
			ISettingsService settings,
			IMessageService messageService,
			IErrorHandler errorHandler,
			IEnumerable<Process> processes)
		{
            _serviceProvider = serviceProvider;
            _settings = settings;
			_messageService = messageService;
			_errorHandler = errorHandler;
            this.processes = processes;

            SelectFolderCommand = new RelayCommand(_folderSelector.SelectFolder);
            OnConfirmClick = new RelayCommand(OnConfirm);

            _folderSelector.PropertyChanged += FolderSelector_PropertyChanged;
        }

		private void OnConfirm()
		{
			_errorHandler.Guard(() =>
			{
				if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
				{
					_messageService.ShowMessage("You need to select options", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				if (!TryExport(exportation, dataType))
				{
					return; // failure already reported; keep the window open for a corrected attempt
				}

				GetAssociatedWindow<DataExportWindow>().DialogResult = true;
			}, "exporting process data");
		}

		internal bool TryExport(ExportationType exportation, DataType dataType)
		{
			return _errorHandler.Guard(() =>
			{
				var exporter = _serviceProvider.GetRequiredService<DataExporterFactory>().CreateDataExporter(dataType);

				switch (exportation)
				{
					case ExportationType.Processes:
						var result = exporter.Export(DirPath, processes);
						if (result.IsSuccess)
						{
							return true;
						}

						_messageService.ShowMessage(
							string.Format(Strings.ExportFailedFormat, DirPath) + " " + DescribeFailure(result.FailureReason!.Value),
							Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
						return false;
				}

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
