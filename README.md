# Task Manager

[![CI](https://github.com/lukegor/TaskManager/actions/workflows/ci.yml/badge.svg)](https://github.com/lukegor/TaskManager/actions/workflows/ci.yml)

A Windows process explorer built with WPF on .NET 10: live process list with low-overhead diffing,
per-process detail enrichment (path/bitness via native APIs), priority management, settings, and
data export (Excel/CSV/JSON/XML/TXT). English and Polish UI.

## Solution layout

```
src/
  TaskManager/           WPF application: views, view models, presentation state
                         (ProcessListCatalog), window orchestration (IWindowService),
                         app services, localization
  TaskManager.Domain/    UI-free core: process snapshots/diff engine, enrichment,
                         OS process operations, exporters, settings model
tests/
  TaskManager.UnitTests/         fast, hermetic test suite (default `dotnet test` target)
  TaskManager.IntegrationTests/  tests against live system state; run explicitly
```

Dependency rule: only the WPF exe references WPF. Domain depends on the BCL plus
NtApiDotNet and ClosedXML.

## Build & run

Requires the .NET SDK pinned in `global.json`.

```bash
dotnet build TaskManager.slnx
dotnet run --project src/TaskManager
```

## Test

```bash
dotnet test tests/TaskManager.UnitTests          # hermetic suite
dotnet test tests/TaskManager.IntegrationTests   # touches real system state
```

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

## Design docs

Architecture decisions and plans live under `docs/superpowers/` (`specs/`, `plans/`).
