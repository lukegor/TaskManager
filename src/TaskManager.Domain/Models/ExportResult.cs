namespace TaskManager.Domain.Models
{
    public enum ExportFailureReason
    {
        IoError,
        AccessDenied,
        InvalidPath
    }

    /// <summary>
    /// Outcome of a data export. Expected IO failures are data, not exceptions.
    /// </summary>
    public sealed record ExportResult
    {
        public string? FilePath { get; private init; }
        public ExportFailureReason? FailureReason { get; private init; }
        public bool IsSuccess => FailureReason is null;

        public static ExportResult Success(string filePath) => new() { FilePath = filePath };

        public static ExportResult Fail(ExportFailureReason reason) => new() { FailureReason = reason };
    }
}
