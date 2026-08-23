using Microsoft.Extensions.Logging;
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
        private readonly ILogger<BaseDataExporter> _logger;

        public BaseDataExporter(IAppSettings settings, ILogger<BaseDataExporter> logger)
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

        /// <summary>
        /// Single source of truth for what counts as an expected export failure.
        /// The catch filter above and the failure classification are one decision:
        /// returns true (with reason) exactly for failures the file-writing APIs document,
        /// false for everything else so genuine defects stay loud and reach Tier 2.
        /// </summary>
        /// <remarks>
        /// Coverage follows the documented exception contracts of the write calls used by
        /// exporters (<see cref="System.IO.File"/>, <see cref="System.Xml.Linq.XDocument.Save"/>,
        /// ClosedXML SaveAs): DirectoryNotFound/DriveNotFound/PathTooLong are IOException
        /// subclasses, ArgumentNull is an ArgumentException subclass. SecurityException
        /// from legacy docs is .NET Framework CAS-only and does not exist on modern .NET.
        /// </remarks>
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
