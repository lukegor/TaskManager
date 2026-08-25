namespace TaskManager.Presentation
{
    public enum RefreshOutcome
    {
        Ok,
        Skipped,
        Failed,
    }

    /// <summary>
    /// Outcome snapshot of the most recent refresh attempt. For non-Ok outcomes
    /// LastDurationMs carries the duration of the LAST COMPLETED (Ok) refresh —
    /// a failed/skipped attempt has no duration of its own.
    /// </summary>
    public sealed record RefreshDiagnostics(
        double LastDurationMs,
        RefreshOutcome Outcome,
        DateTimeOffset CompletedAt);
}
