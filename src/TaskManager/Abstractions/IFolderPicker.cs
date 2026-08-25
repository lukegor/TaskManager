namespace TaskManager.Abstractions
{
    public interface IFolderPicker
    {
        /// <returns>Absolute folder path, or null when the user cancelled.</returns>
        string? PickFolder();
    }
}
