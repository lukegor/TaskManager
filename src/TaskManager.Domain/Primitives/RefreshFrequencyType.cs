namespace TaskManager.Domain.Primitives
{
    /// <summary>
    /// Specifies the frequency at which a refresh operation occurs for processes
    /// </summary>
    public enum RefreshFrequencyType
    {
        High,
        Medium,
        Low,
        Paused,
    }

    public static class RefreshFrequencies
    {
        public static readonly Dictionary<RefreshFrequencyType, int> SecondsMapping = new Dictionary<RefreshFrequencyType, int>
        {
            { RefreshFrequencyType.High, 5 },
            { RefreshFrequencyType.Medium, 7 },
            { RefreshFrequencyType.Low, 10 },
            { RefreshFrequencyType.Paused, 0 },
        };
    }
}
