using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Domain.Services;
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

        public SetPriorityWindow Create(IEnumerable<System.Diagnostics.Process> processes)
        {
            SetPriorityWindow setPriorityWindow = new SetPriorityWindow();

            var processManager = _serviceProvider.GetRequiredService<ProcessManager>();
            var setPriorityWindowVM = new SetPriorityWindowViewModel(processManager, processes);
            setPriorityWindow.DataContext = setPriorityWindowVM;

            return setPriorityWindow;
        }
    }
}
