using System.IO;
using System.Text.Json;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.Data_Export
{
	public class JsonExporter : BaseDataExporter
	{
        protected override string Extension => "json";

        public JsonExporter(IAppSettings settings) : base(settings)
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
