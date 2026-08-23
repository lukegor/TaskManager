using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Utility;
using TaskManager.Services.Factories;
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

		private ExportationType? exportation = null;
		public ExportationType? Exportation
		{
			get { return exportation; }
			set { SetProperty(ref exportation, value); }
		}

		private DataType? dataType = null;
		public DataType? DataType
		{
			get { return dataType; }
			set { SetProperty(ref dataType, value); }
		}

		private string dirPath = string.Empty;
		public string DirPath
		{
			get { return dirPath; }
			set
			{
				if (SetProperty(ref dirPath, value))
				{
					_folderSelector.DirPath = value;
				}
			}
		}

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
        private readonly IAppSettings _settings;
		private readonly IMessageService _messageService;

		public DataExportWindowViewModel(IServiceProvider serviceProvider,
			IAppSettings settings,
			IMessageService messageService,
			IEnumerable<Process> processes)
		{
            _serviceProvider = serviceProvider;
            _settings = settings;
			_messageService = messageService;
            this.processes = processes;

            SelectFolderCommand = new RelayCommand(_folderSelector.SelectFolder);
            OnConfirmClick = new RelayCommand(OnConfirm);

            _folderSelector.PropertyChanged += FolderSelector_PropertyChanged;
        }

		private void OnConfirm()
		{
			if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
			{
                _messageService.ShowMessage("You need to select options", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			var window = GetAssociatedWindow<DataExportWindow>();

			ExportData(exportation, dataType);
			window.DialogResult = true;

		}

		private void ExportData(ExportationType exportation, DataType dataType)
		{
            var exporter = _serviceProvider.GetRequiredService<DataExporterFactory>().CreateDataExporter(dataType);
			switch (exportation)
			{
				case ExportationType.Processes:
					exporter.Export(dirPath, processes);
					break;
			}
		}
	}
}
