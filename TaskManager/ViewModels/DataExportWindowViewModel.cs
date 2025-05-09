using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Task_Manager.Services;
using Task_Manager.Services.Data_Export;
using Task_Manager.UI.Views;
using TaskManager.Domain;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Utility;
using TaskManager.Shared.Resources.Languages;
using TaskManager.Utility.Utility;

namespace Task_Manager.ViewModels
{
	public class DataExportWindowViewModel : ObservableObject
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

		private string dirPath;
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

		private void FolderSelector_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
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

		public DataExportWindowViewModel()
		{
			SelectFolderCommand = new RelayCommand(_folderSelector.SelectFolder);
			OnConfirmClick = new RelayCommand(OnConfirm);

			_folderSelector.PropertyChanged += FolderSelector_PropertyChanged;
		}

		public DataExportWindowViewModel(IServiceProvider serviceProvider, IAppSettings settings, IEnumerable<Process> processes) : this()
		{
            _serviceProvider = serviceProvider;
            _settings = settings;
            this.processes = processes;
		}

		private void OnConfirm()
		{
			if (Exportation == null || DataType == null)
			{
				MessageBox.Show("You need to select options", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			var window = App.Current.Windows.OfType<DataExportWindow>().FirstOrDefault(x => x.DataContext == this);

			ExportData();
			window.DialogResult = true;

		}

		private void ExportData()
		{
            var exporter = _serviceProvider.GetRequiredService<DataExporterFactory>().CreateDataExporter((DataType)dataType);
			switch (Exportation)
			{
				case ExportationType.Processes:
					exporter.Export(dirPath, processes);
					break;
			}
		}
	}
}
