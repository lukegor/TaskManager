using Microsoft.Extensions.DependencyInjection;
using TaskManager.Domain.Abstractions;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Services.ErrorHandling;
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

        public DataExportWindowViewModel Create(IReadOnlyList<Process> data)
        {
            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            var messageService = _serviceProvider.GetRequiredService<IMessageService>();
            var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();

            return new DataExportWindowViewModel(_serviceProvider, settings, messageService, errorHandler, data);
        }
    }
}
