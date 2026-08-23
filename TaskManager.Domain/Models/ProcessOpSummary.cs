namespace TaskManager.Domain.Models
{
    public enum ProcessOpFailureReason
    {
        ProcessExited,
        AccessDenied,
        Unknown
    }

    public sealed record ProcessOpFailure(int Pid, ProcessOpFailureReason Reason);

    /// <summary>
    /// Per-PID outcome of a batch process operation. Expected OS failures are data, not exceptions.
    /// </summary>
    public sealed record ProcessOpSummary
    {
        public static readonly ProcessOpSummary Empty = new();

        public IReadOnlyList<int> SucceededPids { get; init; } = [];
        public IReadOnlyList<ProcessOpFailure> Failures { get; init; } = [];

        public bool HasFailures => Failures.Count > 0;
    }
}
