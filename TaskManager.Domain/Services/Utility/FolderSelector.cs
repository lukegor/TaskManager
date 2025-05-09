using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskManager.Domain.Services.Utility
{
	public class FolderSelector : ObservableObject
	{
		private string dirPath;
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
