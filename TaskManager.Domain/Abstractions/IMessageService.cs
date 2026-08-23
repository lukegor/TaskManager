using System.Windows;

namespace TaskManager.Domain.Abstractions
{
    public interface IMessageService
    {
        MessageBoxResult ShowMessage(string message, string caption, MessageBoxButton buttonSettings, MessageBoxImage icon);
        MessageBoxResult ShowMessage(Window owner, string message, string caption, MessageBoxButton buttonSettings, MessageBoxImage icon);
    }
}
