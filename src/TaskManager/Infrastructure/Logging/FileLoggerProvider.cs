using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TaskManager.Infrastructure.Logging
{
    internal readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        Exception? Exception);

    /// <summary>
    /// Asynchronous daily-rolling file logger. Producers format and enqueue only
    /// (bounded channel, DropOldest); a single drain task owns all file I/O and
    /// flushes whenever the queue momentarily empties. Completing the channel via
    /// Dispose/DisposeAsync flushes pending entries with a bounded wait; app shutdown
    /// reaches it through container disposal (App.OnExit).
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
    {
        private const int DefaultCapacity = 10_000;
        private const int RetentionDays = 7;
        private static readonly string DefaultLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskManager",
            "logs");

        private readonly string _logDirectory;
        private readonly TimeProvider _timeProvider;
        private readonly Channel<LogEntry> _channel;
        private readonly int _capacity;
        private readonly Task _drainTask;

        private long _enqueuedTotal;
        private long _drainedTotal;

        private long _overflowState = -1;      // -1 = idle; 1 = armed (see Enqueue)
        private long _overflowArmEnqueued;
        private long _overflowArmDrained;
        private long _overflowArmCount;

        private StreamWriter? _stream;
        private DateTime _openDate;
        private bool _disposed;        // benign race: worst case is a redundant dispose pass

        public FileLoggerProvider() : this(DefaultLogDirectory)
        {
        }

        public FileLoggerProvider(string logDirectory)
            : this(logDirectory, TimeProvider.System, DefaultCapacity)
        {
        }

        public FileLoggerProvider(string logDirectory, TimeProvider timeProvider)
            : this(logDirectory, timeProvider, DefaultCapacity)
        {
        }

        internal FileLoggerProvider(string logDirectory, TimeProvider timeProvider, int capacity)
        {
            _logDirectory = logDirectory;
            _timeProvider = timeProvider;
            _capacity = capacity;
            _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
            Directory.CreateDirectory(_logDirectory);
            DeleteExpiredLogs();
            _drainTask = DrainAsync();
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

        /// <summary>
        /// Blocking bridge for <see cref="IDisposable"/> teardown by DI containers.
        /// Runs on the caller's thread (the UI thread at App.OnExit) and may wait up
        /// to the same bounded 5 s as <see cref="DisposeAsync"/>; acceptable only on
        /// the terminal shutdown path.
        /// </summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>Completes the channel so the drain flushes pending entries (bounded wait).</summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _channel.Writer.TryComplete();

            try
            {
                await _drainTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // abandon the drain: best-effort shutdown must proceed
            }
            catch (Exception)
            {
                // drain faulted (e.g., stream disposal I/O): terminal path — logging
                // failures must never propagate into application shutdown
            }
        }

        private void Enqueue(in LogEntry entry)
        {
            if (_disposed)
            {
                return; // post-shutdown calls are silent no-ops
            }

            if (_channel.Writer.TryWrite(entry))
            {
                var enqueued = Interlocked.Increment(ref _enqueuedTotal);

                // DropOldest evicts silently; true saturation is visible as a full channel.
                // Edge-triggered: arm once per episode; the drain disarms on full recovery.
                if (_channel.Reader.Count >= _capacity)
                {
                    Volatile.Write(ref _overflowArmDrained, Volatile.Read(ref _drainedTotal));
                    Volatile.Write(ref _overflowArmCount, _channel.Reader.Count);
                    Volatile.Write(ref _overflowArmEnqueued, enqueued);
                    Interlocked.CompareExchange(ref _overflowState, 1, -1); // -1 = idle, 1 = armed
                }
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    Append(entry);
                    Interlocked.Increment(ref _drainedTotal);
                    if (_channel.Reader.Count == 0 && _stream is not null)
                    {
                        ReportOverflowIfArmed();
                        await _stream.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // stream died mid-run: stop consuming quietly; the channel keeps
                // accepting writes, which DropOldest silently evicts once saturated
            }
            finally
            {
                await FlushAndDisposeStreamAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fires when an armed episode recovers: the queue is fully drained. Exact
        /// evictions for the episode = (Δenqueued − Δdrained) + countAtArm.
        /// </summary>
        private void ReportOverflowIfArmed()
        {
            if (Interlocked.Read(ref _overflowState) != 1)
            {
                return;
            }

            var armEnqueued = Volatile.Read(ref _overflowArmEnqueued);
            var armDrained = Volatile.Read(ref _overflowArmDrained);
            var armCount = Volatile.Read(ref _overflowArmCount);

            if (Interlocked.Exchange(ref _overflowState, -1) != 1)
            {
                return; // another thread already closed the episode
            }

            var dropped = (_enqueuedTotal - armEnqueued) - (_drainedTotal - armDrained) + armCount;
            _stream?.WriteLine(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Warning] {nameof(FileLoggerProvider)}: log buffer overflowed; up to {dropped} entries were dropped.");
        }

        private async Task FlushAndDisposeStreamAsync()
        {
            if (_stream is null)
            {
                return;
            }

            try
            {
                await _stream.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // broken disk: nothing further to do
            }

            _stream.Dispose();
            _stream = null;
        }

        private void Append(LogEntry entry)
        {
            var localDate = entry.Timestamp.LocalDateTime.Date;
            if (_stream is null || localDate != _openDate)
            {
                OpenStreamFor(localDate);
            }

            var line = $"{entry.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Category}: {entry.Message}";
            if (entry.Exception is not null)
            {
                line += Environment.NewLine + entry.Exception;
            }

            _stream!.WriteLine(line);
        }

        private void OpenStreamFor(DateTime localDate)
        {
            if (_stream is not null)
            {
                DeleteExpiredLogs(); // retention re-checked at every rollover
                _stream.Dispose();
            }

            _openDate = localDate;
            _stream = new StreamWriter(
                Path.Combine(_logDirectory, $"tm-{localDate:yyyyMMdd}.log"),
                append: true);
        }

        private void DeleteExpiredLogs()
        {
            foreach (var filePath in Directory.GetFiles(_logDirectory, "tm-*.log"))
            {
                if (File.GetLastWriteTime(filePath) <
                    _timeProvider.GetLocalNow().LocalDateTime.AddDays(-RetentionDays))
                {
                    File.Delete(filePath);
                }
            }
        }

        private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                owner.Enqueue(new LogEntry(
                    owner._timeProvider.GetLocalNow(),
                    logLevel,
                    category,
                    formatter(state, exception),
                    exception));
            }
        }
    }
}
