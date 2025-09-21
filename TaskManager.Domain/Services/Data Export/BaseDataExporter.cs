using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services.Data_Export
{
    public abstract class BaseDataExporter
    {
        protected const string FileNamePrefix = @"\record-";
        protected string DateTime => _settings.DateTimeFormat;
        protected abstract string Extension { get; }

        private readonly IAppSettings _settings;

        public BaseDataExporter(IAppSettings settings)
        {
            _settings = settings;
        }

        public void Export<T>(string dirPath, IEnumerable<T> records) where T : IExportable
        {
            IList<string> strings = GetStrings(records).ToList();

            string fullFileName = dirPath + GenerateFileName(Extension);
            PerformExport<T>(fullFileName, strings);
        }

        protected abstract void PerformExport<T>(string fullFileName, IEnumerable<string> strings);

        protected virtual IEnumerable<string> GetStrings<T>(IEnumerable<T> eventRecords) where T : IExportable
        {
            foreach (T record in eventRecords)
            {
                yield return record.ToDelimitedString(' ');
            }
        }

        protected string GenerateFileName(string extension)
        {
            return $"{FileNamePrefix}{System.DateTime.Now.ToString(DateTime)}.{extension}";
        }
    }
}
