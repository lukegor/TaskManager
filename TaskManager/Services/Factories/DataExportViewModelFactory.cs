using Microsoft.Extensions.DependencyInjection;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.ViewModels;

namespace TaskManager.Services.Factories
{
    internal class DataExportViewModelFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DataExportViewModelFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public DataExportWindowViewModel Create(IEnumerable<Process> data)
        {
            var settings = _serviceProvider.GetRequiredService<IAppSettings>();
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();

            return new DataExportWindowViewModel(_serviceProvider, settings, messageService, data);
        }
    }
}
