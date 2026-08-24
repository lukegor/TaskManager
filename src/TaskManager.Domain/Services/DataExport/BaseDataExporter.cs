using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services.DataExport
{
    public abstract class BaseDataExporter
    {
        protected const string FileNamePrefix = @"\record-";
        protected string DateTime => _settings.Current.DateTimeFormat;
        protected abstract string Extension { get; }

        private readonly ISettingsService _settings;
        private readonly ILogger<BaseDataExporter> _logger;

        public BaseDataExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public ExportResult Export<T>(string dirPath, IEnumerable<T> records) where T : IExportable
        {
            try
            {
                IList<string> strings = GetStrings(records).ToList();

                string fullFileName = dirPath + GenerateFileName(Extension);
                PerformExport<T>(fullFileName, strings);
                return ExportResult.Success(fullFileName);
            }
            catch (Exception ex) when (TryClassifyFailure(ex, out var reason))
            {
                _logger.LogWarning(ex, "Export as {Extension} failed ({Reason})", Extension, reason);
                return ExportResult.Fail(reason);
            }
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

        private static bool TryClassifyFailure(Exception ex, out ExportFailureReason reason)
        {
            switch (ex)
            {
                case ArgumentException or NotSupportedException:
                    reason = ExportFailureReason.InvalidPath;
                    return true;
                case UnauthorizedAccessException:
                    reason = ExportFailureReason.AccessDenied;
                    return true;
                case IOException:
                    reason = ExportFailureReason.IoError;
                    return true;
                default:
                    reason = default;
                    return false;
            }
        }
    }
}
