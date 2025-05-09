using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Task_Manager.ViewModels;
using TaskManager.Domain;
using TaskManager.Domain.Models;

namespace Task_Manager.Services.Factories
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
