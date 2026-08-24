using System.IO;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
    public class TxtExporter : BaseDataExporter
    {
        protected override string Extension => "txt";

        public TxtExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger)
        {
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }
    }
}
