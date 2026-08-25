# Async File Logging & Log Conventions — Design

- **Date:** 2026-08-25
- **Status:** Approved (brainstorming session)
- **Scope items:** L1 (async/buffered file logger) + L2 (logging conventions) from `docs/engineering-infrastructure-backlog.md`
- **Depends on:** H1 (graceful shutdown) — landed in Wave 1; `App.OnExit` disposes the DI container, which disposes this provider.

---

## 1. Problem

`FileLoggerProvider.Write` performs synchronous disk I/O under a lock on every log call, on the calling thread — including the UI thread during dispatcher-batch refreshes. A slow disk stalls the app; a burst of logs multiplies the cost line-by-line. Additionally, operation outcomes are under-logged: batch process operations (`ProcessOpSummary`) produce no log entry at all, and the refresh pipeline emits no per-tick trace, leaving nothing for a future diagnostics surface (D2) to consume.

## 2. Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Overflow behavior | Never block producers; drop oldest entries (bounded channel, DropOldest); detect saturation episodes via enqueue/drain totals and emit one bounded recovery notice per episode |
| L2 extent | Missing structured log statements in code + "Logging" conventions section in README (no standalone doc) |
| Dependencies | Zero new packages (Channel is BCL); L2 tests use a hand-rolled logger spy instead of Microsoft.Extensions.Diagnostics.Testing |
| LoggerMessage source-gen | Stays deferred (CA1848 waiver unchanged); plain `ILogger` extension methods |

## 3. L1 — Architecture

```
ILogger.Log (any thread, incl. UI)
   │ format line cheaply, stamp DateTimeOffset via TimeProvider.GetLocalNow()
   ▼
Channel<LogEntry>  bounded capacity 10_000 · FullMode=DropOldest · SingleReader
   │ TryWrite — never blocks; on miss Interlocked.Increment(_droppedCount)
   ▼
single background drain task: ReadAllAsync → Append → StreamWriter(AutoFlush=false)
   │ reader momentarily empty → FlushAsync()
   │ entry date ≠ open file date → ROLLOVER: dispose stream, open tm-yyyyMMdd.log,
   │   DeleteExpiredLogs(), append drop notice if _droppedCount > 0 (then reset)
   ▼
%LOCALAPPDATA%\TaskManager\logs\tm-yyyyMMdd.log  — line format unchanged:
"yyyy-MM-dd HH:mm:ss.fff [Level] Category: message" (+ exception.ToString() on following lines)
```

### Components

**`LogEntry`** (new, same file or adjacent — implementer's choice within `TaskManager.Infrastructure.Logging`):

```csharp
internal readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception);
```

**`FileLoggerProvider`** (rewritten):

```csharp
public sealed class FileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    public FileLoggerProvider();                                        // default dir + TimeProvider.System
    public FileLoggerProvider(string logDirectory);                     // TimeProvider.System
    public FileLoggerProvider(string logDirectory, TimeProvider timeProvider);
    internal FileLoggerProvider(string logDirectory, TimeProvider timeProvider, int capacity); // tests
}
```

State: `_channel`/`_reader`, `_drainTask`, `_disposed`, enqueue/drain totals (for saturation detection), current `StreamWriter` + its open date, `TimeProvider`. The drain task starts in the constructor and runs until the channel completes. There is deliberately NO `_broken` flag: any I/O fault escapes `Append`, is caught by the drain loop's filtered catch, and the loop simply exits — the channel then absorbs writes that are never consumed. Same observable behavior, fewer moving parts.

Producer path (`FileLogger.Log` → provider): format via the existing formatter contract, build `LogEntry`, `TryWrite`. No locks anywhere on the producer side.

Drain loop (`DrainAsync`, owns all file I/O):

```
try {
  await foreach entry in _reader.ReadAllAsync():
     Append(entry);                      // rollover check inside
     ReportOverflowIfRecovered();        // see below
     if (_reader.Count == 0) await stream.FlushAsync();
} catch (IOException / UnauthorizedAccessException) { /* drain exits; channel absorbs further writes */ }
finally { final flush if possible; dispose stream; }
```

- **Rollover:** compare entry's local date to open file's date; on change close stream, open new daily file, run retention cleanup (moved from constructor-only to every rollover).
- **Overflow detection & reporting:** `DropOldest` evicts silently inside the channel, so exact drop counting is impossible. The provider instead tracks enqueue/drain totals: when backlog (`enqueued − drained`) reaches capacity, a saturation episode has occurred; the drain records the target total and, once drained past it (queue recovered), appends one direct line `[Warning] FileLoggerProvider: log buffer overflowed; up to {n} entries were dropped.` (n = worst-case eviction bound at report time), then re-arms for future episodes. One notice per episode.
- **Stream failure:** filtered catch ends the drain quietly; subsequent `TryWrite` calls still succeed into the unconsumed channel. Logging never throws, never crashes the app.

Shutdown:

- `DisposeAsync()`: idempotent guard → `_writer.TryComplete()` → `await _drainTask.WaitAsync(TimeSpan.FromSeconds(5))` (timeout tolerated; AggregateException from cancel/fault swallowed) → done (stream disposal owned by task's `finally`).
- `Dispose()` (sync, required by `ILoggerProvider`): `DisposeAsync().AsTask().GetAwaiter().GetResult()` — blocking is acceptable on the terminal path already wired through `App.OnExit`.
- There is deliberately NO live `FlushAsync()` seam: every test assertion (including "buffered content not visible before shutdown") is deterministic through `DisposeAsync` alone, and removing the live seam eliminates the design's only racy piece. If D2 later needs mid-life flushing, it can be added safely then.

Wiring: `App.ConfigureServices` keeps `new FileLoggerProvider()` (parameterless); no DI change needed.

## 4. L1 — Error Handling Summary

| Failure | Behavior |
|---|---|
| Buffer overflow | Drop oldest, count; single notice line appended on queue recovery |
| Disk write fails mid-run | `_broken` flag; drain exits silently; app unaffected |
| Shutdown drain timeout (>5 s) | Abandon task, proceed with exit |
| Double dispose / dispose while logging | Idempotent; writes into completed channel are no-ops |
| Producer-side exceptions | Impossible by construction |

## 5. L1 — Testing

All assertions are `DisposeAsync`-centered (deterministic; no live-flush seam):

Rewritten existing tests:

- `Write_AppendsFormattedMessageToDailyFile`, `Write_IncludesExceptionDetails` → log → `await provider.DisposeAsync()` → assert identical content/format as today.
- Retention-at-construction test: unchanged.

New tests:

1. `Write_PersistsThroughShutdownFlush` - entries survive the bounded-wait shutdown flush. Mid-life visibility is deliberately NOT asserted: flush-on-idle makes it scheduling-dependent. The no-sync-I/O-on-caller-thread guarantee is structural (the producer only enqueues into the channel), not timing-proven.
2. `Dispose_FlushesPendingEntries` — multiple entries across categories survive shutdown without any explicit flush call.
3. `Dispose_IsIdempotent` — double dispose throws nothing, single drain outcome.
4. `Rollover_CreatesNewDailyFile_AndRunsRetention` — `FakeTimeProvider` advanced across midnight between entries; second file created, stale files deleted at rollover.
5. `Overflow_ReportsExactlyOneNotice_PerEpisode` — small-capacity internal ctor; burst past capacity; oldest entries lost; exactly one overflow notice after recovery; a second burst produces exactly one more.
6. `Drain_SurvivesStreamFailure` — locked target directory forces write errors; no exception escapes any API; provider disposes cleanly.

## 6. L2 — Code Changes

**`ProcessOperationsService`** — after each batch loop completes, one structured Information entry:

```csharp
_logger.LogInformation("Batch {Operation} completed: {SucceededCount} succeeded, {FailedCount} failed",
    operationName, summary.SucceededPids.Count, summary.Failures.Count);
if (summary.HasFailures && _logger.IsEnabled(LogLevel.Debug))
{
    foreach (var failure in summary.Failures)
        _logger.LogDebug("Batch {Operation}: PID {Pid} failed ({Reason})",
            operationName, failure.Pid, failure.Reason);
}
```

(`operationName`: `"terminate"` / `"set-priority"` literal passed by each method.) Existing per-PID success Debug lines stay guarded as they are.

**`ProcessListCatalog.RefreshCoreAsync`** — guarded Debug tick trace using counts from the diff already computed:

```csharp
var startTimestamp = _timeProvider.GetTimestamp();
// ... existing pipeline ...
if (_logger.IsEnabled(LogLevel.Debug))
{
    var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
    var diff = batch.Batch;
    _logger.LogDebug("Refresh completed in {ElapsedMs:F1} ms: {AddedCount} added, {RemovedCount} removed, {UpdatedCount} updated",
        elapsed.TotalMilliseconds, diff.Added.Count, diff.Removed.Count, diff.Updated.Count);
}
```

This trace is the intended data source for D2 (diagnostics window).

**Test support:** `tests/TaskManager.UnitTests/TestSupport/RecordingLogger.cs` — minimal spy `ILogger` capturing (level, category, formatted message, named placeholder state) with lookup helpers; used to assert both L2 entries deterministically without real files.

New tests: terminate/set-priority summary logged with correct counts and operation name; per-PID expected-failure Debug emitted only when enabled; refresh trace carries duration + three counts (via existing catalog test harness patterns).

## 7. README Conventions Section ("Logging")

Content outline (placed after "Test"):

- **Levels:** Debug = hot-path/pipeline detail (guarded); Information = lifecycle milestones and operation outcomes; Warning = recovered failure with context; Error/Critical = failures needing attention. Fatal reserved for terminal handlers.
- **Templates:** static literal strings with named PascalCase placeholders (`{Pid}`, `{ElapsedMs}`); never interpolate values into the message string; pass exception as first argument, not placeholder.
- **Categories:** `ILogger<T>` of the owning type.
- **Cost control:** wrap expensive-to-evaluate Debug/Trace arguments in `if (_logger.IsEnabled(LogLevel.X))`.
- **Infrastructure note:** logging is asynchronous (bounded buffer, drops oldest under overflow — see design doc); LoggerMessage source generation (CA1848) deliberately deferred.

## 8. Out of Scope

- LoggerMessage/[LoggerMessage] source-gen adoption (CA1848 waiver stays)
- Diagnostics window / status bar surfaces (D1/D2)
- Log viewer UI, external sinks, scopes (`BeginScope` stays null)
- Release packaging/changelog (B5/B6, separate wave)

## 9. Acceptance Criteria

1. No log call ever performs synchronous disk I/O on the calling thread (structural: producer only enqueues).
2. Pending entries are durable across normal shutdown (flush-on-dispose verified by test).
3. Daily rollover and retention work under a controllable clock; line format byte-compatible with today's.
4. Burst overload degrades by dropping old entries, never blocking, with exactly one bounded notice per saturation episode.
5. Every batch process operation produces one structured Information summary; every refresh tick produces a guarded Debug trace with duration and add/remove/update counts.
6. README documents the conventions; full suite green under warnings-as-errors; zero new package dependencies.
