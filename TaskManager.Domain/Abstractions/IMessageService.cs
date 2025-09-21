using System.Windows;

namespace TaskManager.Domain.Abstractions
{
    public interface IMessageService
    {
        public abstract MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttonSettings, MessageBoxImage icon);
        public abstract MessageBoxResult ShowMessage(Window owner, string message, string caption, MessageBoxButton buttonSettings, MessageBoxImage icon);

    }
}
