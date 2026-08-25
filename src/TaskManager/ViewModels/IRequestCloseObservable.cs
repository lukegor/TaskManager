namespace TaskManager.ViewModels
{
    /// <summary>Implemented by dialog VMs; the hosting window closes when this fires.</summary>
    internal interface IRequestCloseObservable
    {
        event EventHandler? RequestClose;
    }
}
