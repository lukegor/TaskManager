using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Domain;
using TaskManager.Utility.Utility;

namespace Task_Manager.Services.Data_Export
{
    public class DataExporterFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DataExporterFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public BaseDataExporter CreateDataExporter(DataType dataType)
        {
            var settings = _serviceProvider.GetRequiredService<IAppSettings>();

            return dataType switch
            {
                DataType.Csv => new CsvExporter(settings),
                DataType.Txt => new TxtExporter(settings),
                DataType.Xlsx => new ExcelExporter(settings),
                DataType.Json => new JsonExporter(settings),
                DataType.Xml => new XmlExporter(settings),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            };
        }
    }
}
