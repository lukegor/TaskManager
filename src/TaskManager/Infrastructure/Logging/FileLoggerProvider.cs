using System.IO;
using Microsoft.Extensions.Logging;

namespace TaskManager.Infrastructure.Logging
{
    /// <summary>
    /// Minimal daily-rolling file logger. One file per day inside the log directory,
    /// retention-limited on construction. Every write is appended under a lock,
    /// so no explicit flush is needed anywhere.
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private const int RetentionDays = 7;
        private static readonly string DefaultLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskManager",
            "logs");

        private readonly string _logDirectory;
        private readonly object _writeLock = new();

        public FileLoggerProvider() : this(DefaultLogDirectory)
        {
        }

        public FileLoggerProvider(string logDirectory)
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(_logDirectory);
            DeleteExpiredLogs();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this, categoryName);
        }

        public void Dispose()
        {
        }

        internal void Write(LogLevel logLevel, string category, string? message, Exception? exception)
        {
            var fileName = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_writeLock)
            {
                File.AppendAllText(fileName, line + Environment.NewLine);
            }
        }

        private void DeleteExpiredLogs()
        {
            foreach (var filePath in Directory.GetFiles(_logDirectory, "tm-*.log"))
            {
                if (File.GetLastWriteTime(filePath) < DateTime.Now.AddDays(-RetentionDays))
                {
                    File.Delete(filePath);
                }
            }
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _owner;
            private readonly string _category;

            public FileLogger(FileLoggerProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _owner.Write(logLevel, _category, formatter(state, exception), exception);
            }
        }
    }
}
