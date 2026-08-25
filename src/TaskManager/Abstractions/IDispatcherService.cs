namespace TaskManager.Abstractions
{
    /// <summary>
    /// Abstraction over <see cref="System.Windows.Application.Current.Dispatcher"/>.
    /// Presentation concern; intentionally lives in the app, not Domain.
    /// </summary>
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
