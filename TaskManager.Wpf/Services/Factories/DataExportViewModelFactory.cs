using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.ViewModels;
using TaskManager.Domain.Models;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Services.Factories
{
    public class DataExportViewModelFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DataExportViewModelFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public DataExportWindowViewModel Create(IEnumerable<Process> data)
        {
            var settings = _serviceProvider.GetRequiredService<IAppSettings>();

            return new DataExportWindowViewModel(_serviceProvider, settings, data);
        }
    }
}
