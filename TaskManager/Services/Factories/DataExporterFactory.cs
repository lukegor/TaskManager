using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services.Data_Export;
using TaskManager.Utility.Utility;

namespace TaskManager.Services.Factories
{
    internal class DataExporterFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DataExporterFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public virtual BaseDataExporter CreateDataExporter(DataType dataType)
        {
            var settings = _serviceProvider.GetRequiredService<IAppSettings>();
            var exporterLogger = _serviceProvider.GetRequiredService<ILogger<BaseDataExporter>>();

            return dataType switch
            {
                DataType.Csv => new CsvExporter(settings, exporterLogger),
                DataType.Txt => new TxtExporter(settings, exporterLogger),
                DataType.Xlsx => new ExcelExporter(settings, exporterLogger),
                DataType.Json => new JsonExporter(settings, exporterLogger),
                DataType.Xml => new XmlExporter(settings, exporterLogger),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            };
        }
    }
}
