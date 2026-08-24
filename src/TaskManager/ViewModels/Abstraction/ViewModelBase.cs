using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace TaskManager.ViewModels.Abstraction
{
    /// <summary>
    /// Base class for all ViewModels
    /// </summary>
    internal class ViewModelBase : ObservableObject
    {
        protected TWindow GetAssociatedWindow<TWindow>() where TWindow : Window
        {
            var associatedWindow = App.Current.Windows.OfType<TWindow>().First(window => window.DataContext == this);
            return associatedWindow;
        }
    }
}
