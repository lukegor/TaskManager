using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Task_Manager.Utility;

namespace Task_Manager.Services.Data_Export
{
    internal static class DataExporterFactory
    {
        public static BaseDataExporter CreateDataExporter(DataType dataType)
        {
            return dataType switch
            {
                DataType.Csv => new CsvExporter(),
                DataType.Txt => new TxtExporter(),
                DataType.Xlsx => new ExcelExporter(),
                DataType.Json => new JsonExporter(),
                DataType.Xml => new XmlExporter(),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            };
        }
    }
}
