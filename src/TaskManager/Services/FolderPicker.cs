using Microsoft.Win32;
using TaskManager.Abstractions;

namespace TaskManager.Services
{
    internal sealed class FolderPicker : IFolderPicker
    {
        public string? PickFolder()
        {
            var dialog = new OpenFolderDialog();
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
