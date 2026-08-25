using TaskManager.Abstractions;

namespace TaskManager.Services
{
    /// <summary>
    /// Implements both the app-level and (transitionally) the legacy Domain-level
    /// dispatcher abstractions; a single instance serves both registrations.
    /// The Domain interface disappears once its last consumer does.
    /// </summary>
    public class WpfDispatcherService : IDispatcherService, global::TaskManager.Domain.Abstractions.IDispatcherService
    {
        public void Invoke(Action action)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(action);
        }
    }
}
