namespace TaskManager.Domain.Abstractions
{
    /// <summary>
    /// Abstraction over <see cref="System.Windows.Application.Current.Dispatcher"/>
    /// </summary>
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
