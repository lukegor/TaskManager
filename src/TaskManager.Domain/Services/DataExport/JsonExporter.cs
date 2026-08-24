using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
	public class JsonExporter : BaseDataExporter
	{
        protected override string Extension => "json";

        public JsonExporter(ISettingsService settings, ILogger<BaseDataExporter> logger) : base(settings, logger)
        {
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            string jsonString = JsonSerializer.Serialize(strings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(fullFileName, jsonString);
        }
    }
}
