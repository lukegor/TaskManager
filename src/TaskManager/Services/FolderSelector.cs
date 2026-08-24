using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace TaskManager.Services
{
	public class FolderSelector : ObservableObject
	{
		private string dirPath = string.Empty;
		public string DirPath
		{
			get { return dirPath; }
			set { SetProperty(ref dirPath, value); }
		}

		public void SelectFolder()
		{
			OpenFolderDialog openFolderDialog = new OpenFolderDialog();
			if (openFolderDialog.ShowDialog() == true)
			{
				DirPath = openFolderDialog.FolderName;
			}
			else DirPath = string.Empty;
		}
	}
}
