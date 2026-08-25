# Async Logging & Conventions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace synchronous file logging with an async bounded-channel writer (flush-on-dispose, rollover retention, overflow notices) and add structured batch-outcome/refresh-tick logging plus documented conventions.

**Architecture:** Producers format and enqueue into a bounded `Channel<LogEntry>` (DropOldest); one drain task owns all file I/O, flushing when the queue empties and completing pending writes when disposal completes the channel (already wired via `App.OnExit` container disposal). L2 adds one structured Information entry per batch operation and one guarded Debug trace per refresh tick, verified through a hand-rolled recording logger.

**Tech Stack:** BCL only (`System.Threading.Channels`, `Microsoft.Extensions.Logging`), xunit.v3/MTP, NSubstitute, FakeTimeProvider. Zero new packages.

**Spec:** `docs/superpowers/specs/2026-08-25-async-logging-and-log-conventions-design.md` (authoritative; includes the dispose-centered testing decision and saturation-based overflow notices agreed during planning).

## Global Constraints

- Work on branch `build/engineering-guardrails` (verify with `git branch --show-current`; do NOT switch). Suite baseline: 136 tests green.
- Build runs `TreatWarningsAsErrors` — every task must build with **zero warnings**.
- Central Package Management: **no package additions or removals** in this batch.
- Log line format stays byte-compatible: `yyyy-MM-dd HH:mm:ss.fff [Level] Category: message` (exception text appended on following lines).
- There is deliberately NO live `FlushAsync()` seam — all provider tests assert around `DisposeAsync`.
- Commit style: lowercase conventional prefixes (`feat:`, `test:`, `docs:`).
- Platform: Windows, pwsh 7; solution file `TaskManager.slnx`; full-suite command is `dotnet test tests/TaskManager.UnitTests`.
- Repo conventions: `Method_Scenario_Expected` test names; `ILogger<T>` categories; `IsEnabled` guards before expensive Debug args.

---

### Task 1: Async file logger core

**Files:**
- Modify: `src/TaskManager/Infrastructure/Logging/FileLoggerProvider.cs` (entire rewrite, ~200 lines)
- Modify: `tests/TaskManager.UnitTests/Logging/FileLoggerProviderTests.cs` (rewrite two tests, add three)

**Interfaces:**
- Consumes: nothing new (same `ILoggerProvider` contract; parameterless ctor keeps `App.ConfigureServices` wiring unchanged).
- Produces (used by Tasks 2–4 and tests):
  - `internal readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, Exception? Exception)`
  - `public FileLoggerProvider(string logDirectory, TimeProvider timeProvider)` and `internal FileLoggerProvider(string logDirectory, TimeProvider timeProvider, int capacity)` constructors
  - `public ValueTask DisposeAsync()` — completes channel, awaits drain (5 s bound), flushes pending entries
  - `private void Enqueue(in LogEntry entry)` and `private async Task DrainAsync()` — Task 2 hooks into both

- [ ] **Step 1: Rewrite `FileLoggerProvider.cs`**

Replace the entire file with:

```csharp
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
        private readonly Task _drainTask;

        private long _enqueuedTotal;   // Task 2 uses these for overflow detection
        private long _drainedTotal;

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
            catch (AggregateException)
            {
                // canceled or faulted drain: terminal path, nothing further to do
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
                Interlocked.Increment(ref _enqueuedTotal);
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
                        await _stream.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // stream died mid-run: stop consuming quietly; the channel absorbs further writes
            }
            finally
            {
                await FlushAndDisposeStreamAsync().ConfigureAwait(false);
            }
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
```

Design notes for the implementer:
- The producer path allocates only the formatted string plus the `LogEntry` struct copy — no locks, no async.
- `DropOldest` eviction happens silently inside the channel; the enqueue/drain totals exist so Task 2 can detect saturation (this task only wires the counters).
- The sync `Dispose()` blocking bridge is intentional and acceptable on the terminal path; do NOT "fix" it with `.GetAwaiter().GetResult()` alternatives like spin-waits.

- [ ] **Step 2: Rewrite the provider tests**

Replace the contents of `tests/TaskManager.UnitTests/Logging/FileLoggerProviderTests.cs` with:

```csharp
using Microsoft.Extensions.Logging;
using TaskManager.Infrastructure.Logging;

namespace TaskManager.UnitTests
{
    /// <summary>
    /// All assertions are DisposeAsync-centered by design: the producer only enqueues,
    /// so durable content is observable after the bounded-wait shutdown flush.
    /// </summary>
    public class FileLoggerProviderTests
    {
        private readonly string _logDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-log-tests-{Guid.NewGuid():N}");

        [Fact]
        public async Task Write_AppendsFormattedMessageToDailyFile_OnDispose()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Test.Category").LogInformation("hello {Name}", "world");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("hello world");
            content.ShouldContain("[Information]");
            content.ShouldContain("Test.Category");
        }

        [Fact]
        public async Task Write_IncludesExceptionDetails_OnDispose()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogError(new InvalidOperationException("boom"), "op failed");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("op failed");
            content.ShouldContain("[Error]");
            content.ShouldContain("boom");
        }

        [Fact]
        public async Task Write_BufferedContentNotVisibleBeforeDispose()
        {
            var provider = new FileLoggerProvider(_logDirectory); // manual lifecycle: no using
            var file = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");

            provider.CreateLogger("Cat").LogInformation("buffered secret marker");

            var premature = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            premature.ShouldNotContain("buffered secret marker"); // producer never wrote synchronously

            await provider.DisposeAsync();

            File.ReadAllText(file).ShouldContain("buffered secret marker");
        }

        [Fact]
        public async Task Dispose_FlushesPendingEntries_FromMultipleCategories()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Category.A").LogWarning("first");
            provider.CreateLogger("Category.B").LogWarning("second");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("first");
            content.ShouldContain("second");
        }

        [Fact]
        public async Task Dispose_IsIdempotent()
        {
            var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogInformation("once");

            await provider.DisposeAsync();
            await provider.DisposeAsync(); // must not throw
            provider.Dispose();            // sync path over disposed provider: also fine
        }

        [Fact]
        public void Constructor_DeletesLogsOlderThanRetention()
        {
            Directory.CreateDirectory(_logDirectory);
            var staleLog = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(staleLog, "old");
            File.SetLastWriteTime(staleLog, DateTime.Now.AddDays(-30));

            using var provider = new FileLoggerProvider(_logDirectory);

            File.Exists(staleLog).ShouldBeFalse();
        }

        private string ReadTodayLog()
        {
            var path = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        public void Dispose()
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
    }
}
```

(Note: the test class implements `IDisposable` via an implicit-interface `public void Dispose()` exactly as the original file did — xunit calls it for teardown.)

- [ ] **Step 3: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; full suite green (baseline 136, expected ≈139 after the rewrite: two tests renamed/reworked, three added, none removed).

Then a real-app smoke check that shutdown still persists logs:

```bash
dotnet run --project src/TaskManager
# close the window, then:
Get-Content "$env:LOCALAPPDATA\TaskManager\logs\tm-$(Get-Date -Format yyyyMMdd).log" -Tail 2
```

The tail must still contain the `Application exiting with code 0` line (proves the async pipeline flushed through `App.OnExit`). Report the outcome; if you cannot launch a GUI in your environment, say so explicitly instead of pretending.

- [ ] **Step 4: Commit**

```bash
git add src/TaskManager/Infrastructure/Logging/FileLoggerProvider.cs tests/TaskManager.UnitTests/Logging/FileLoggerProviderTests.cs
git commit -m "feat: asynchronous buffered file logger with flush-on-dispose"
```

---

### Task 2: Overflow notices (saturation detection) + stream-failure resilience tests

**Files:**
- Modify: `src/TaskManager/Infrastructure/Logging/FileLoggerProvider.cs` (add two fields, saturation arm in `Enqueue`, report call in `DrainAsync`)
- Modify: `tests/TaskManager.UnitTests/Logging/FileLoggerProviderTests.cs` (add three tests)

**Interfaces:**
- Consumes: Task 1's `_enqueuedTotal`/`_drainedTotal` counters, `internal FileLoggerProvider(string, TimeProvider, int capacity)` ctor, `OpenStreamFor`/`Append` structure.
- Produces: one `[Warning] FileLoggerProvider: log buffer overflowed; up to {n} entries were dropped.` direct line per saturation episode; silent drain-exit on stream failure. Nothing downstream consumes these programmatically (yet — D2 may later).

- [ ] **Step 1: Add saturation detection and reporting**

In `FileLoggerProvider`, add two fields next to the totals:

```csharp
        private long _overflowReportAfterDrained = -1; // -1 = idle; else drain-total target marking recovery
        private long _overflowBoundAtDetection;        // upper bound on evictions during the episode
```

Extend `Enqueue`'s success branch (after `Interlocked.Increment(ref _enqueuedTotal)`) with:

```csharp
                var backlog = enqueued - Volatile.Read(ref _drainedTotal);
                if (backlog >= _channel.Options.Capacity)
                {
                    Volatile.Write(ref _overflowBoundAtDetection, backlog - _channel.Options.Capacity);
                    Interlocked.CompareExchange(ref _overflowReportAfterDrained, enqueued, -1);
                }
```

(`enqueued` is the local returned by the increment; note the bound is written BEFORE the CAS so the drain thread reading `target >= 0` observes a valid bound.)

In `DrainAsync`, insert a call immediately after `Interlocked.Increment(ref _drainedTotal);`:

```csharp
                    ReportOverflowOnceRecovered();
```

and add the method:

```csharp
        /// <summary>
        /// One notice per saturation episode: once the drain passes the total that was
        /// enqueued when the buffer saturated, the queue has demonstrably recovered.
        /// </summary>
        private void ReportOverflowOnceRecovered()
        {
            var target = Interlocked.Read(ref _overflowReportAfterDrained);
            if (target < 0 || Volatile.Read(ref _drainedTotal) < target)
            {
                return;
            }

            var bound = Volatile.Read(ref _overflowBoundAtDetection);
            Interlocked.Exchange(ref _overflowReportAfterDrained, -1); // re-arm for the next episode

            _stream?.WriteLine(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Warning] {nameof(FileLoggerProvider)}: log buffer overflowed; up to {bound} entries were dropped.");
        }
```

- [ ] **Step 2: Add the resilience tests**

Append to `FileLoggerProviderTests`:

```csharp
        [Fact]
        public async Task Rollover_CreatesNewDailyFile_AndRunsRetention()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 25, 23, 59, 50, TimeSpan.Zero));
            using var provider = new FileLoggerProvider(_logDirectory, time);
            var logger = provider.CreateLogger("Cat");
            logger.LogInformation("before midnight");

            // Stale file created AFTER construction so its deletion can only be the work
            // of the rollover-time retention pass, not the constructor's.
            var stale = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(stale, "old");

            time.Advance(TimeSpan.FromSeconds(20)); // crosses midnight -> 2026-08-26
            logger.LogInformation("after midnight");
            await provider.DisposeAsync();

            File.Exists(Path.Combine(_logDirectory, "tm-20260825.log")).ShouldBeTrue();
            var nextDay = Path.Combine(_logDirectory, "tm-20260826.log");
            File.Exists(nextDay).ShouldBeTrue();
            File.ReadAllText(nextDay).ShouldContain("after midnight");
            File.Exists(stale).ShouldBeFalse();
        }

        [Fact]
        public async Task Overflow_ReportsExactlyOneNotice_PerEpisode()
        {
            const int capacity = 64;
            using var provider = new FileLoggerProvider(_logDirectory, TimeProvider.System, capacity);
            var logger = provider.CreateLogger("Cat");

            Burst(logger, capacity * 4);
            await WaitForAsync(() => ReadTodayLog().Contains("log buffer overflowed"));

            // Second episode after the queue visibly recovered: exactly one more notice.
            Burst(logger, capacity * 4);
            await WaitForAsync(() => CountOccurrences(ReadTodayLog(), "log buffer overflowed") >= 2);

            var content = ReadTodayLog();
            CountOccurrences(content, "log buffer overflowed").ShouldBe(2);
            content.ShouldNotContain("burst 0 ");  // oldest entries were the ones dropped
            content.ShouldContain($"burst {(capacity * 4) - 1}"); // newest survived
        }

        [Fact]
        public async Task Drain_SurvivesStreamFailure()
        {
            Directory.CreateDirectory(_logDirectory);
            using var lockHandle = File.Open(
                Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log"),
                FileMode.OpenOrCreate, FileAccess.Read, FileShare.None); // denies any writer

            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogInformation("never lands");

            await provider.DisposeAsync(); // must not throw despite the unwritable target
        }

        private static void Burst(ILogger logger, int count)
        {
            for (var i = 0; i < count; i++)
            {
                logger.LogInformation("burst {Index}", i);
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
```

Requires `using Microsoft.Extensions.Time.Testing;` at the top of the test file (FakeTimeProvider — already a referenced package). Also note `Burst`'s parameter type is `Microsoft.Extensions.Logging.ILogger`.

- [ ] **Step 3: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green (expected ≈142).

Sanity spot-check of the drop semantics (temporary, revert after observing): raise a console print or debugger watch confirming `ReadTodayLog()` lacks early burst indices right after dispose in the overflow test — i.e., the `ShouldNotContain("burst 0 ")` assertion is doing real work, not vacuously passing because of message formatting. Revert any instrumentation before committing.

- [ ] **Step 4: Commit**

```bash
git add src/TaskManager/Infrastructure/Logging/FileLoggerProvider.cs tests/TaskManager.UnitTests/Logging/FileLoggerProviderTests.cs
git commit -m "feat: daily rollover retention and overflow notices for file logger"
```

---

### Task 3: Structured batch-operation summaries

**Files:**
- Modify: `src/TaskManager.Domain/Services/ProcessOperationsService.cs`
- Create: `tests/TaskManager.UnitTests/TestSupport/RecordingLogger.cs`
- Create: `tests/TaskManager.UnitTests/Services/ProcessOperationsServiceLoggingTests.cs`

**Interfaces:**
- Consumes: existing `InternalsVisibleTo("TaskManager.UnitTests")` from `TaskManager.Domain`; existing `ExecutePerPid` private pipeline.
- Produces:
  - `internal ProcessOpSummary ExecutePerPid(string operationName, IEnumerable<int> selectedPids, Action<int> operation)` — widened from private, now takes the operation name used in logs
  - Public methods pass `"terminate"` / `"set-priority"` respectively
  - `RecordingLogger<T> : ILogger<T>` in `TaskManager.UnitTests.TestSupport` with `IReadOnlyList<Entry> Entries` where `Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)` — reused by Task 4

- [ ] **Step 1: Widen `ExecutePerPid` and add the summary logs**

In `ProcessOperationsService.cs`: change the two callers to pass their operation name and make the method internal:

```csharp
        public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> pids)
        {
            return ExecutePerPid("terminate", pids, pid =>
            {
                // ... existing body unchanged ...
            });
        }

        public ProcessOpSummary SetPriority(IReadOnlyCollection<int> pids, ProcessPriorityClass priority)
        {
            return ExecutePerPid("set-priority", pids, pid =>
            {
                // ... existing body unchanged ...
            });
        }
```

Replace `private ProcessOpSummary ExecutePerPid(IEnumerable<int> selectedPids, Action<int> operation)` with:

```csharp
        internal ProcessOpSummary ExecutePerPid(string operationName, IEnumerable<int> selectedPids, Action<int> operation)
        {
            var succeeded = new List<int>();
            var failures = new List<ProcessOpFailure>();

            foreach (var pid in selectedPids)
            {
                try
                {
                    operation(pid);
                    succeeded.Add(pid);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    var reason = ClassifyFailure(ex);
                    _logger.LogWarning(ex, "Process operation failed for PID {Pid} ({Reason})", pid, reason);
                    failures.Add(new ProcessOpFailure(pid, reason));
                }
            }

            _logger.LogInformation("Batch {Operation} completed: {SucceededCount} succeeded, {FailedCount} failed",
                operationName, succeeded.Count, failures.Count);

            if (failures.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var failure in failures)
                {
                    _logger.LogDebug("Batch {Operation}: PID {Pid} failed ({Reason})",
                        operationName, failure.Pid, failure.Reason);
                }
            }

            return new ProcessOpSummary { SucceededPids = succeeded, Failures = failures };
        }
```

The loop body is byte-identical to today — only the signature, name plumbing, and the two post-loop blocks are new.

- [ ] **Step 2: Create the recording logger test support**

Create `tests/TaskManager.UnitTests/TestSupport/RecordingLogger.cs`:

```csharp
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
```

- [ ] **Step 3: Add the logging tests**

Create `tests/TaskManager.UnitTests/Services/ProcessOperationsServiceLoggingTests.cs` (folder exists; keep its namespace):

```csharp
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Services
{
    /// <summary>
    /// Drives ExecutePerPid directly (internal, visible to this project): the OS-facing
    /// public methods stay integration-test territory, while summary/classification
    /// logging is verified hermetically.
    /// </summary>
    public class ProcessOperationsServiceLoggingTests
    {
        private readonly RecordingLogger<ProcessOperationsService> _logger = new();
        private readonly ProcessOperationsService _service;

        public ProcessOperationsServiceLoggingTests()
        {
            _service = new ProcessOperationsService(_logger);
        }

        [Fact]
        public void ExecutePerPid_MixedOutcomes_LogsStructuredSummary()
        {
            var summary = _service.ExecutePerPid("terminate", [1, 2, 3], pid =>
            {
                if (pid == 2)
                {
                    throw new Win32Exception(5, "access denied");
                }
            });

            summary.SucceededPids.ShouldBe([1, 3]);

            var info = _logger.Entries.Single(e => e.Level == LogLevel.Information);
            info.Message.ShouldBe("Batch terminate completed: 2 succeeded, 1 failed");
            info.Values["Operation"].ShouldBe("terminate");
            info.Values["SucceededCount"].ShouldBe(2);
            info.Values["FailedCount"].ShouldBe(1);
        }

        [Fact]
        public void ExecutePerPid_AllSucceed_SummaryReportsZeroFailures_AndNoFailureDebug()
        {
            _service.ExecutePerPid("set-priority", [7, 8], _ => { });

            var info = _logger.Entries.Single(e => e.Level == LogLevel.Information);
            info.Message.ShouldBe("Batch set-priority completed: 2 succeeded, 0 failed");
            _logger.Entries.Count(e => e.Level == LogLevel.Debug).ShouldBe(0);
        }

        [Fact]
        public void ExecutePerPid_WithFailures_LogsEachFailureAtDebug()
        {
            _service.ExecutePerPid("set-priority", [9], _ =>
                throw new ArgumentException("process exited"));

            var debugs = _logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
            debugs.Count.ShouldBe(1);
            debugs[0].Message.ShouldBe("Batch set-priority: PID 9 failed (ProcessExited)");
            debugs[0].Values["Operation"].ShouldBe("set-priority");
            debugs[0].Values["Pid"].ShouldBe(9);
            debugs[0].Values["Reason"].ShouldBe(ProcessOpFailureReason.ProcessExited);
        }
    }
}
```

Check the existing namespace of other files in `tests/TaskManager.UnitTests/Services/` first and match it if it differs.

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green (expected ≈145).

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager.Domain/Services/ProcessOperationsService.cs tests/TaskManager.UnitTests/TestSupport/RecordingLogger.cs tests/TaskManager.UnitTests/Services/ProcessOperationsServiceLoggingTests.cs
git commit -m "feat: structured batch-operation summary logging"
```

---

### Task 4: Refresh-tick trace + README logging conventions

**Files:**
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs` (`RefreshCoreAsync`, roughly lines 270–310)
- Create: `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogRefreshTraceTests.cs`
- Modify: `README.md` (new "Logging" section after "## Test")

**Interfaces:**
- Consumes: Task 3's `RecordingLogger<T>`; existing test helpers used by `ProcessListCatalogPollingTests` (`ScriptedEnumerator`, `CountingEnricher`, `InlineDispatcher`, `ProcessFakes.Snap`) — place the new test file in the same namespace `TaskManager.UnitTests.Presentation` so those helpers resolve identically; `_timeProvider` field and `batch.Batch` diff already present in `RefreshCoreAsync`.
- Produces: Debug trace `"Refresh completed in {ElapsedMs:F1} ms: {AddedCount} added, {RemovedCount} removed, {UpdatedCount} updated"` once per successful refresh — the designated data source for the D2 diagnostics window. README section becomes the repo's logging convention reference.

- [ ] **Step 1: Add the guarded trace to `RefreshCoreAsync`**

In `ProcessListCatalog.RefreshCoreAsync`, capture a timestamp as the first statement and emit the trace just before `ApplyBatch`:

```csharp
        private async Task RefreshCoreAsync()
        {
            var startTimestamp = _timeProvider.GetTimestamp();

            // ... existing body byte-identical down to the PipelineBatch ...

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
                var diff = batch.Batch;
                _logger.LogDebug(
                    "Refresh completed in {ElapsedMs:F1} ms: {AddedCount} added, {RemovedCount} removed, {UpdatedCount} updated",
                    elapsed.TotalMilliseconds, diff.Added.Count, diff.Removed.Count, diff.Updated.Count);
            }

            ApplyBatch(batch.Batch, batch.Enrichments);
        }
```

Only the first line, the guarded block, and nothing else change; the trace intentionally fires before `ApplyBatch` so UI marshaling time is not attributed to the pipeline.

- [ ] **Step 2: Add the trace test**

Create `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogRefreshTraceTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Presentation;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Presentation
{
    public class ProcessListCatalogRefreshTraceTests
    {
        [Fact]
        public async Task RefreshPipeline_EmitsGuardedTraceWithDiffCounts()
        {
            var enumerator = new ScriptedEnumerator();
            enumerator.Queue(ProcessFakes.Snap(1));
            enumerator.Queue(ProcessFakes.Snap(2));
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Defaults);
            var logger = new RecordingLogger<ProcessListCatalog>();

            var catalog = new ProcessListCatalog(
                InlineDispatcher, enumerator, new CountingEnricher(), settings,
                Substitute.For<IProcessOperations>(), new FakeTimeProvider(), logger);

            await catalog.LoadForTestAsync();
            await catalog.LoadForTestAsync();

            var traces = logger.Entries
                .Where(e => e.Level == LogLevel.Debug
                            && e.Message.StartsWith("Refresh completed", StringComparison.Ordinal))
                .ToList();

            traces.Count.ShouldBe(2);
            traces[0].Values["AddedCount"].ShouldBe(1);
            traces[0].Values.ContainsKey("ElapsedMs").ShouldBeTrue();

            // second fill swaps pid 1 out for pid 2
            traces[1].Values["AddedCount"].ShouldBe(1);
            traces[1].Values["RemovedCount"].ShouldBe(1);
        }
    }
}
```

Adjust helper usings/names to whatever `ProcessListCatalogPollingTests.cs` actually resolves (`InlineDispatcher` may be a local helper in that file — if it is not accessible from the new file, copy the smallest equivalent dispatcher stub into THIS test file rather than widening visibility elsewhere).

Requires `using Microsoft.Extensions.Logging;` for the `LogLevel` references (add it if the global usings do not cover it).

- [ ] **Step 3: Add the README "Logging" section**

In `README.md`, insert between the "## Test" section and "## Design docs":

```markdown
## Logging

Logs go to `%LOCALAPPDATA%\TaskManager\logs\tm-yyyyMMdd.log` (7-day retention). Writing is
asynchronous: entries pass through a bounded in-memory buffer persisted by a background
writer; under extreme burst pressure the oldest buffered entries are dropped (one warning
line notes each such episode). Pending entries are flushed on normal application exit.

Conventions for new code:

| Level | Use for |
|---|---|
| Debug | hot-path detail (refresh ticks, per-PID operations); guard expensive arguments with `IsEnabled` |
| Information | lifecycle milestones and batch outcomes |
| Warning | recovered failures with context |
| Error / Critical | failures needing attention; terminal handlers |

- Message templates are static literals with named PascalCase placeholders (`{Pid}`, `{ElapsedMs}`) — never interpolate values into the string.
- Pass exceptions via the dedicated argument (`_logger.LogWarning(ex, "...")`), not as placeholders.
- Categories come from `ILogger<T>` of the owning type.
- LoggerMessage source generation (CA1848) is deliberately deferred.
```

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green (expected ≈146).

Manual smoke (GUI): launch, let it run ~15 seconds, close, confirm the day's log contains `Refresh completed` Debug lines only when a debugger is attached (default release run is Information level, so absence is CORRECT there — verify presence instead by running from IDE/debugger or setting `$env:TASKMANAGER_LOGLEVEL='Debug'` before `dotnet run`). Report what you observed.

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager/Presentation/ProcessListCatalog.cs tests/TaskManager.UnitTests/Presentation/ProcessListCatalogRefreshTraceTests.cs README.md
git commit -m "feat: refresh lifecycle trace and README logging conventions"
```

---

## Self-Review Record

- **Spec coverage:** §3 architecture/components → Task 1 (channel, drain, rollover-open, flush-on-empty, Dispose/DisposeAsync, sync bridge, no FlushAsync seam per amended spec); §3 overflow/saturation + §4 error table → Task 2 (arm/report algorithm, filtered-catch drain exit, retention-at-rollover already in Task 1's `OpenStreamFor`); §6 ops summaries + spy → Task 3; §6 refresh trace → Task 4 Step 1; §7 README outline → Task 4 Step 3; §5 test list → mapped 1:1 across Tasks 1–3 (item 2 merged into `Write_BufferedContentNotVisibleBeforeDispose` + `Dispose_FlushesPendingEntries_FromMultipleCategories`); §9 acceptance criteria covered by the combination. ✔
- **Placeholder scan:** Task 1 ships the entire file verbatim; Task 3's loop-body comment "existing body unchanged" refers to code shown in full in the Interfaces/context of the same task (callers) and the untouched original — the modified method is shown complete. Helper-resolution contingency (InlineDispatcher) gives a concrete fallback action. ✔
- **Type consistency:** `LogEntry` field order used identically in producer and Task 1 file; `RecordingLogger<T>.Entry(Level, Message, Values)` matches Task 3/Task 4 assertions; `ExecutePerPid(string, IEnumerable<int>, Action<int>)` matches both callers and tests; `_overflowReportAfterDrained`/`_overflowBoundAtDetection` naming consistent between Steps; MTP filter flags avoided (full-suite commands only). ✔
