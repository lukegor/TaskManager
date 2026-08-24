using System.IO;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
    public class CsvExporter : BaseDataExporter
    {
	    private const char Separator = ',';
        protected override string Extension => "csv";

        public CsvExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger)
        {
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }

        protected override IEnumerable<string> GetStrings<T>(IEnumerable<T> eventRecords)
        {
	        foreach (T record in eventRecords)
	        {
		        yield return record.ToDelimitedString(Separator);
	        }
        }
	}
}
