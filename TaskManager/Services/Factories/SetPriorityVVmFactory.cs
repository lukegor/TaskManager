using Microsoft.Extensions.DependencyInjection;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Services.Factories
{
    public class SetPriorityVVmFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public SetPriorityVVmFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public SetPriorityWindow Create(IEnumerable<int> processes)
        {
            SetPriorityWindow setPriorityWindow = new SetPriorityWindow();

            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var processManager = _serviceProvider.GetRequiredService<ProcessManager>();
            var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();
            var setPriorityWindowVM = new SetPriorityWindowViewModel(messageService, processManager, processes, errorHandler);
            setPriorityWindow.DataContext = setPriorityWindowVM;

            return setPriorityWindow;
        }
    }
}
