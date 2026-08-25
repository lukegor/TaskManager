using Microsoft.Extensions.Logging;

namespace TaskManager.UnitTests.TestSupport
{
    /// <summary>
    /// Captures structured log calls in memory. Placeholder values survive in
    /// Entry.Values keyed by template name ("{OriginalFormat}" excluded), enabling
    /// assertions on both rendered messages and structured fields. IsEnabled returns
    /// true for every level so guarded code paths execute under test.
    /// </summary>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values);

        private readonly List<Entry> _entries = [];
        private readonly object _lock = new();

        public IReadOnlyList<Entry> Entries
        {
            get { lock (_lock) { return [.. _entries]; } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    if (pair.Key != "{OriginalFormat}")
                    {
                        values[pair.Key] = pair.Value;
                    }
                }
            }

            lock (_lock)
            {
                _entries.Add(new Entry(logLevel, formatter(state, exception), values));
            }
        }
    }
}
