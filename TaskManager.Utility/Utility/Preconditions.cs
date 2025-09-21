namespace TaskManager.Utility.Utility
{
    [Flags]
    public enum Preconditions
    {
        None = 0,
        SelectedAnyProcess = 1 << 1,
        GotConfirmation = 1 << 2,
    }
}
