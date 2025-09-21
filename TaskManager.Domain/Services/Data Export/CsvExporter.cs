using System.IO;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.Data_Export
{
    public class CsvExporter : BaseDataExporter
    {
	    private const char Separator = ',';
        protected override string Extension => "csv";

        public CsvExporter(IAppSettings settings)
            : base(settings)
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
