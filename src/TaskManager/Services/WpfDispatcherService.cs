using TaskManager.Domain.Abstractions;

namespace TaskManager.Services
{
    /// <inheritdoc cref="IDispatcherService"/>
    public class WpfDispatcherService : IDispatcherService
    {
        public void Invoke(Action action)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(action);
        }
    }
}
