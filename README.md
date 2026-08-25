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

## Design docs

Architecture decisions and plans live under `docs/superpowers/` (`specs/`, `plans/`).
