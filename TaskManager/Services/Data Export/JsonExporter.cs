using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Task_Manager.Services.Data_Export
{
	internal class JsonExporter : BaseDataExporter
	{
        protected override string Extension => "json";

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
