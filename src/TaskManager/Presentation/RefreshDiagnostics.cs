namespace TaskManager.Presentation
{
    public enum RefreshOutcome
    {
        Ok,
        Skipped,
        Failed,
    }

    /// <summary>Outcome snapshot of the most recent refresh attempt (any of the three outcomes).</summary>
    public sealed record RefreshDiagnostics(
        double LastDurationMs,
        RefreshOutcome Outcome,
        DateTimeOffset CompletedAt);
}
