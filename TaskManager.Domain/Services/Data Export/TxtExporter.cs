using System.IO;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.Data_Export
{
    public class TxtExporter : BaseDataExporter
    {
        protected override string Extension => "txt";

        public TxtExporter(IAppSettings settings) : base(settings)
        {
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }
    }
}
