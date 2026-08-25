# Hygiene & Modernization Batch (Shortlist #2–#11) — Design

**Date:** 2026-08-25
**Status:** Approved
**Scope:** Post-redesign cleanup batch: localization gap, dead bindings, UI-thread responsiveness, composition-root organization, model-mapping centralization, exporter log categories, deterministic polling tests, WindowService STA tests, legacy settings retirement, test-folder casing.
**Explicitly out of scope:** runtime language switching without restart (shortlist #1) — separate future cycle.

## Problem

The architecture redesign left eleven ranked follow-ups. This batch addresses #2–#11. Individually small, but done literally several would violate correctness or testability standards (sync OS calls freezing the UI; polling tests via real timers = flakes), so each fix lands at its modern best-practice shape.

Approaches considered:

- **A. Literal fixes** — rejected: leaves UI freezes (#4) and flaky timer tests (#8).
- **B. Integrated modernization bundle (chosen)** — every item compile-enforced or deterministically testable.
- **C. Broader sweep incl. #1/CI** — rejected as scope creep.

## 1. Correctness quick wins

**Localized validation message.** New resx key pair `SelectOptionsRequired`:
- `Strings.resx`: "You need to select options"
- `Strings.pl.resx`: "Musisz wybrać opcje"

`DataExportWindowViewModel.OnConfirm` replaces the hardcoded literal with `Strings.SelectOptionsRequired`.

**Dead binding removal.** Delete `MainWindowViewModel.MonitoringButtonIcon` (zero XAML references; only declaration remains). Remove the `System.Windows.Media` using when orphaned.

## 2. Responsive process operations

`IProcessListCatalog` changes:

```csharp
Task<ProcessOpSummary> TerminateProcessesAsync(IReadOnlyCollection<int> pids);
Task<ProcessOpSummary> SetPriorityAsync(IReadOnlyCollection<int> pids, ProcessPriorityClass priority);
```

- Implementation wraps the `_processOps` call in `Task.Run`; sync variants are deleted (no other callers).
- **Threading contract:** priority writeback runs *after* the await, resuming on the caller's synchronization context. VM commands execute on the UI thread, so INPC mutations stay on the UI thread — today's invariant, preserved by construction.
- `TerminateProcessesAsync` mutates no state; summary flows back for reporting.

ViewModel wiring:

- `MainWindowViewModel.TerminateCommand`, `SetPriorityCommand` → `AsyncRelayCommand` bodies wrapped in `_errorHandler.GuardAsync(...)`.
- `SetPriorityWindowViewModel.OnConfirmCommand` → `AsyncRelayCommand`; confirm flow awaits `SetPriorityAsync`, then reports partial failures, sets `Confirmed`, raises `RequestClose`.
- WPF auto-disables bound buttons while an async command runs (`ICommand.CanExecuteChanged`) — no overlay/busy UI (YAGNI).
- Selection precondition and confirmation prompt remain synchronous pre-checks before the awaited operation.

## 3. Composition root & model mapping

**Composition split.** New folder `src/TaskManager/Infrastructure/Composition/`:

- `CoreServicesRegistration.AddCoreServices(this IServiceCollection)` — settings store + service, dispatcher service, process enumerator, enricher, `ProcessOperationsService` + `IProcessOperations`, `ProcessListCatalog` + interface, `TimeProvider`.
- `UiServicesRegistration.AddUiServices(this IServiceCollection)` — message service, error handler, folder picker, exporter factory delegate, window service, viewmodels, views.

`App.ConfigureServices` reduces to logging setup + `AddCoreServices()` + `AddUiServices()`. App.xaml.cs becomes a pure bootstrapper.

**Mapping single-source-of-truth.** `Domain.Models.Process` gains:

```csharp
public static Process FromSnapshot(ProcessSnapshot snapshot, ProcessEnrichment enrichment);
public void ApplySnapshot(ProcessSnapshot snapshot);
public Process DeepCopy();
```

Catalog's private `Materialize`, `ApplySnapshot`, `CopyOf` are deleted; all three call sites use the model members. Future process fields get compile-time enforcement via `required` members and one mapping location.

## 4. Exporter log categories

`BaseDataExporter` constructor takes non-generic `ILogger`; derived exporters declare typed loggers (`ILogger<CsvExporter>`, `ILogger<TxtExporter>`, `ILogger<ExcelExporter>`, `ILogger<JsonExporter>`, `ILogger<XmlExporter>`), so file-log categories name the concrete format instead of `BaseDataExporter` for everything. The composition-root switch resolves the matching typed logger per branch.

The `Func<DataType, BaseDataExporter>` delegate stays exactly as is — it is composition-root-owned DI, not service location.

## 5. Deterministic polling tests & WindowService STA tests

**TimeProvider-driven polling.** `ProcessListCatalog` gains a constructor-injected `TimeProvider` (DI registers `TimeProvider.System`). The loop constructs its tick source from that provider: `new PeriodicTimer(interval, timeProvider)` (.NET 9+ overload).

Unit tests add package `Microsoft.Extensions.TimeProvider.Testing` (central version 10.0.11 in `Directory.Packages.props`; reference added to `TaskManager.UnitTests.csproj`). New `ProcessListCatalogPollingTests` drive `FakeTimeProvider.Advance(...)` to cover:

1. first capture happens after exactly one interval,
2. interval change mid-loop takes effect on the next period (old period fires nothing),
3. `Paused` stops ticking; switching back starts a fresh loop,
4. manual refresh restarts the polling phase (next tick measured from manual completion),
5. skip-if-in-flight still holds under advanced time.

Assertions synchronize through awaited condition helpers (poll until enumerator `CallCount` reaches the expected value, bounded by a generous real-time timeout as a failure backstop) — no sleeps on the happy path, no real timer dependency.

**WindowService STA tests.** WindowService splits into internal factories and thin public methods:

```csharp
internal (SettingsWindow Window, SettingsWindowViewModel ViewModel) CreateSettingsDialog();
internal (DataExportWindow Window, DataExportWindowViewModel ViewModel) CreateExportDialog(IReadOnlyList<Process> processes);
internal (SetPriorityWindow Window, SetPriorityWindowViewModel ViewModel) CreatePriorityDialog(IReadOnlyCollection<int> pids);
```

Public `Show*` methods build via the factories and only add `ShowDialog()` / close-relay subscription. `[WpfFact]` tests (pattern proven by `GridTestHost`): assert DataContext types are wired to correctly constructed ViewModels, and that raising the ViewModel's `RequestClose` fires the window's `Closed` event — proving relay subscription without ever showing a dialog. `ShowSetPriority` returns the ViewModel's `Confirmed` flag.

## 6. Housekeeping

**Legacy settings retirement.** Delete `LegacyUserConfigSettings.cs` and `LegacySettingsMigrator.cs`. `JsonSettingsStore` keeps exactly two constructors — the production one taking only `ILogger<JsonSettingsStore>` (LocalAppData path) and the test one `(string directory, ILogger)`; the `_legacyReader` field, `Func` plumbing, and `LoadLegacyOrDefaults` collapse so a missing file simply returns defaults. Retire `JsonSettingsStoreTests` cases covering migration promotion and `LegacySettingsMigrator.ParseFrequency`.

**Test folder casing.** Rename `tests/TaskManager.UnitTests/Viewmodels` → `ViewModels` using two-step case-only `git mv` (`Viewmodels` → `tmp_rename` → `ViewModels`). Namespaces already say `UnitTests.ViewModels`; zero code edits.

## Migration Order

1. Quick wins: resx key + literal replacement, delete `MonitoringButtonIcon`, folder casing rename.
2. Legacy settings retirement (files, store trim, test retirement).
3. Model mapping onto `Process`; catalog call sites switch over.
4. Composition root split into registration extensions.
5. Exporter typed loggers.
6. Async catalog operations + async VM commands.
7. TimeProvider polling injection + new polling tests; WindowService factory split + STA tests.
8. Full verification sweep: clean build, hermetic suite, integration suite, boundary greps (no `IServiceProvider` outside App.xaml.cs; Domain free of presentation types).

Each numbered step is independently committable and testable.

## Testing Impact

- Suite grows by: polling dynamics (5 scenarios), WindowService wiring (~4 assertions groups), plus existing suites adjusted (async command awaits; legacy tests removed).
- All new tests hermetic or STA-without-dialog; integration suite untouched except unaffected.
- No behavior regressions expected: language of validation message localized (pl users gain correct text); terminate/priority flows identical modulo responsiveness.

## Non-Goals

- No runtime language switching (#1 stays separate).
- No busy overlays/progress UI beyond native button disabling.
- No exporter format/output changes; no keyed-DI migration for exporters.
- No CI pipeline work.
