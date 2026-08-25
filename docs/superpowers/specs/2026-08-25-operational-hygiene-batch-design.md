# Operational Hygiene Batch (#1, #3–#6) — Design

**Date:** 2026-08-25
**Status:** Approved
**Scope:** Five small post-batch findings: self-healing polling loop, async export path, honest PID typing, config-driven log floor, resx template cleanup.
**Out of scope:** runtime localization (#1 of the earlier shortlist), BetterDataGrid/UI.Controls audit (separate dedicated pass), FileLoggerProvider write buffering.

Section-to-finding map: §Self-healing = finding #1; §Async export = finding #3; §PID typing = finding #4; §Log floor = finding #5; §resx cleanup = finding #6.

## 1. Self-healing polling loop

`ProcessListCatalog.RunPollingLoopAsync` currently exits on any unexpected exception; polling stays dead until a settings change or manual refresh. Restructure:

```csharp
private async Task RunPollingLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var seconds = CurrentIntervalSeconds;
            if (seconds == 0) return; // paused: settings change restarts a fresh loop
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), _timeProvider);
            while (await timer.WaitForNextTickAsync(ct))
                await SafePollingRefreshAsync();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Polling iteration failed; restarting loop");
            await Task.Delay(TimeSpan.FromSeconds(1)); // uncancelled on purpose: shutdown lingers <=1s
        }
    }
}
```

- Semantics preserved: paused still exits (resume via settings change starts a fresh loop); manual refresh still restarts the phase.
- The 1s backoff uses **real time deliberately**: the error path is defense-in-depth, not behavior under test; no FakeTimeProvider coupling for it.
- Existing polling tests must stay green unchanged.

## 2. Async export path

`DataExportWindowViewModel`:

```csharp
public AsyncRelayCommand OnConfirmClick { get; }
internal Task<bool> TryExportAsync(DataType dataType);
```

- `TryExportAsync` wraps `_exporterFactory(dataType).Export(DirPath, _processes)` in `Task.Run` and keeps all modeled-failure reporting identical. Sync `TryExport` is deleted.
- Exporters are stateless per call (they read `settings.Current` during export); running them on a worker thread is safe.
- Rationale: ClosedXML workbook generation can take seconds — same UI-freeze class already fixed for kill/priority operations.

Test updates: export VM suite (`TryExport_Success/Failure/FactoryCrash/MaterializedSnapshot` become async awaiting `TryExportAsync`; direct-call tests only) and `WindowServiceTests.CreateExportDialog_SeedsWithMaterializedProcesses` awaits `OnConfirmClick.ExecuteAsync`.

## 3. Honest PID typing

`Process.Pid`: generated property type changes from `int?` to `int`.

- Every construction site assigns a PID (`FromSnapshot`, `DeepCopy`, tests, `GridTestHost.CreateItems`) — snapshots guarantee one.
- `MainWindowViewModel.GetSelectedPids` drops the `Convert.ToInt32` wrapper: `.Select(x => x.Process.Pid)`.
- Grep sweep at implementation time for any remaining null-handling around Pid (`Pid ==`, `Pid.HasValue`, `Convert.ToInt32`).

## 4. Config-driven log floor

In `App.OnStartup`'s logging setup, replace hardcoded `SetMinimumLevel(LogLevel.Debug)`:

```
minimum = TASKMANAGER_LOGLEVEL env var parsed as LogLevel
       -> if absent: Debug when System.Diagnostics.Debugger.IsAttached
       -> else Information
```

No appsettings.json infrastructure for a single knob (YAGNI). Invalid env values fall back to the debugger-based default with a startup warning logged after the provider exists (or simply ignored pre-logger — implementation may parse before configuring and skip the warning).

## 5. resx template junk

Delete the Visual Studio template entries from `Strings.resx`: `Name1`, `Color1`, `Bitmap1`, `Icon1` (and accompanying `Comment1`/`Text1` schema samples if present). Verify first that `Strings.Designer.cs` contains no generated properties referencing them (expected: none — they predate the generator); if any exist, remove those properties in the same commit. Check `Strings.pl.resx` for the same junk and treat identically.

## Migration Order

1. resx cleanup (finding #6)
2. Log floor (finding #5)
3. PID typing (finding #4)
4. Async export (finding #3)
5. Self-healing loop (finding #1)
6. Verification sweep: clean build, unit suite ×2 (stability), integration suite, boundary greps (`Convert.ToInt32` gone; `TryExport(` sync gone)

Each step independently committable.

## Testing Impact

- New tests: none required beyond conversions; existing suites updated for awaited export calls and PID typing.
- Polling suite must pass unchanged after the loop restructure (its scenarios encode the preserved semantics).
- Integration suite untouched.

## Non-Goals

- No busy overlay for exports beyond native button disabling.
- No buffered/async file logger.
- No appsettings pipeline.
