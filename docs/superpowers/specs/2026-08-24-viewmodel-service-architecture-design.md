# ViewModel / Service / Application Architecture Redesign — Design

**Date:** 2026-08-24
**Status:** Approved
**Scope:** Layer boundaries between TaskManager.Domain and the WPF app; ownership of view state, polling, window orchestration, and process operations; ViewModel dependency hygiene

## Problem

An audit of the current design found structural issues that undermine testability, honesty of layer boundaries, and maintainability:

- **F1 — `ProcessManager` is a god-service, and "Domain" isn't a domain.**
  `src/TaskManager.Domain/Services/ProcessManager.cs` owns five responsibilities: state storage (`ObservableCollection` + PID index + enrichment cache), refresh pipeline orchestration, polling orchestration, OS process operations (kill/set-priority), and UI-thread marshaling via `IDispatcherService`. The Domain project references an abstraction of the WPF Dispatcher and `ObservableCollection` — presentation concepts. The project is not a domain layer; it is a mixed core/presentation library whose name misleads.
- **F2 — Service Locator everywhere.** `MainWindowViewModel`, `DataExportWindowViewModel`, and all three factories take raw `IServiceProvider` and call `GetRequiredService` at runtime. Dependencies are invisible in signatures and untestable without building a container.
- **F3 — ViewModels own Windows.** VMs resolve windows from DI, set `DataContext`, call `ShowDialog()`, and close themselves via `ViewModelBase.GetAssociatedWindow<T>()`, which scans `App.Current.Windows` matching `DataContext == this`. Fragile global-state lookup; VMs cannot be tested headless.
- **F4 — Startup side effects in a VM constructor.** `MainWindowViewModel`'s constructor fire-and-forgets process loading and starts polling. Constructors with async side effects are untestable and order-dependent.
- **F5 — Latent DI bug.** `App.xaml.cs` registers `SettingsService` twice as two independent singletons (`ISettingsService → SettingsService` and concrete `SettingsService`). Resolving concrete vs interface yields different instances with divergent `Current`/`Changed`. Currently benign only because nothing resolves the concrete type.
- **F6 — Anemic/over-built pieces.** `TimerManager` wraps `System.Timers.Timer` adding no value (and forces an `async void` event hop); `FolderSelector` is an ObservableObject requiring a two-way property sync dance with its VM; `Preconditions` flags enum guards exactly one flag plus commented-out dead code; three factories are pure `IServiceProvider` wrappers; `ReportPartialFailures` is duplicated in two VMs; a single-case `switch` remains in `TryExport`.

## Goal

Two honest layers with real boundaries: pure logic in `TaskManager.Domain`, all presentation concerns (view state, dispatcher batching, polling, window orchestration) in the WPF app. ViewModels depend only on interfaces they can list in their constructors. Every VM is constructible headless with fakes.

Approaches considered:

- **A. Minimal cleanup** — fix F5, remove dead code, dedupe helpers; keep service locator and VM-owned windows. Rejected: leaves F1–F4 untouched.
- **B. Presentation/Core split + honest interfaces (chosen)** — keep the two projects but make boundaries real.
- **C. Full Clean Architecture** — application layer with use-case handlers/mediator/DTOs. Rejected per YAGNI at ~4k LOC.

## Target Architecture

### 1. Two honest layers, same projects

**`TaskManager.Domain` — pure logic only:**

- `NtSystemProcessEnumerator` (enumeration), `ProcessDiffEngine.Compute` (already pure), `ProcessEnricher`, exporter family, settings primitives/models, `ProcessSnapshot`.
- New stateless `ProcessOps` service (Domain): kill/set-priority against OS PIDs; returns `ProcessOpSummary`; failure classification moves here unchanged.
- **Removed from Domain:** `IDispatcherService` and `ObservableCollection` usage — the boundary rule is that Domain contains no presentation/WPF-coupled types.
- **INPC on `Process` stays, deliberately.** DataGrid in-place cell updates depend on per-field `INotifyPropertyChanged` on the row model; replacing rows per tick would lose selection state and cause churn. `INotifyPropertyChanged` is a BCL type (System.ComponentModel), not a WPF dependency, so keeping it does not violate the boundary rule above. Documented here as an explicit decision, not an oversight.

**`TaskManager` app gains a presentation component `ProcessListCatalog`:**

- Owns `ObservableCollection<ProcessItem>` (wrapped read-only), PID index, enrichment cache — everything that must mutate on the UI thread.
- Pipeline per tick: capture snapshot via enumerator → off-thread diff/enrich (pure Domain calls) → one dispatcher batch applying removes/adds/in-place updates. Semantics preserved exactly from today's `ProcessManager`.
- Exposes `Items`, `ProcessCount` (INPC), `LoadProcesses()`, `StartPolling()`, `PerformRefresh(isUserInitiated)`, `SnapshotForExport()`, selection-based operations delegated to `ProcessOps`.

`ProcessManager` dissolves into `ProcessListCatalog` + `ProcessOps`. No behavior change intended.

### 2. Polling without TimerManager

Delete `TimerManager`. One `PeriodicTimer` loop inside `ProcessListCatalog`:

- Interval initialized from settings; settings change restarts the loop.
- Tick skipped when a refresh is in flight (`SemaphoreSlim.Wait(0)`); user-initiated refresh waits on the gate and restarts polling afterwards — current semantics preserved.
- No `async void` event hop; loop exceptions logged, never fatal to the app.

### 3. Window orchestration out of ViewModels

New app-level `IWindowService` implemented in the composition root:

```csharp
public interface IWindowService
{
    void ShowSettings();                                 // returns after dialog closes (restart decision stays in caller flow)
    void ShowExport(IReadOnlyList<Process> processes);
    bool ShowSetPriority(IReadOnlyCollection<int> pids); // true = confirmed & applied
}
```

Message boxes (`MessageBox.Show` variants) stay in the existing app-level `IMessageService`; `IWindowService` is strictly window orchestration.

- ViewModels never touch `Window`, `DataContext`, or `App.Current.Windows`. `ViewModelBase.GetAssociatedWindow<T>()` dies; `ViewModelBase` shrinks to (or is replaced by) CommunityToolkit `ObservableObject`.
- The three factories (`DataExportViewModelFactory`, `SetPriorityVVmFactory`, `DataExporterFactory`) are deleted. Window + VM construction lives in the composition root (`WindowService` implementation).
- Exporter creation: keyed DI or a plain switch inside the composition root; exporters stay transient/stateless-per-call.

### 4. ViewModel constructor hygiene

- No `IServiceProvider` parameters anywhere. All dependencies are explicit interfaces: `ISettingsService`, `IMessageService`, `IErrorHandler`, `IProcessListCatalog` (new app abstraction over `ProcessListCatalog`), `IWindowService`, and (DataExport only) `IFolderPicker`.
- No side effects in constructors. Startup load/poll begins via an explicit `[RelayCommand] InitializeAsync` invoked exactly once by the composition root after `MainWindow` creation (not fire-and-forget in a ctor).
- Language-change-restart check stays in the main VM flow but reads settings before/after `ShowSettings()` via `ISettingsService` — no direct window knowledge needed beyond the service call.

### 5. Small deletions/dedups

- Partial-failure reporting deduplicated: one shared helper formats `ProcessOpSummary` into a localized message; both VMs use it via `IMessageService`.
- Delete `Preconditions` flags enum; replace with a plain guard method returning bool.
- Replace `FolderSelector` ObservableObject sync dance with `IFolderPicker.PickFolder() : string?` (wraps `OpenFolderDialog`); DataExport VM binds `DirPath` directly.
- Fix double `SettingsService` registration (single forwarding registration: `AddSingleton<SettingsService>()` + `AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>())`).
- Drop single-case `switch` in `TryExport`; keep modeled `ExportResult` handling.
- Remove duplicated `using TaskManager.Services;` in App.xaml.cs and commented-out code blocks.

### 6. What does NOT change

- Diff engine, enricher, exporter family, `ExportResult` model, settings pipeline (validate → persist → swap → notify), error-handler tiers (`UiErrorHandler` + global handlers), localization approach, CommunityToolkit source-generated observable properties, test project split (UnitTests default, IntegrationTests explicit).

## Ownership Map (before → after)

| Current | Destination |
|---|---|
| `Domain/Services/ProcessManager` (storage + pipeline + batch apply) | App: `Presentation/ProcessListCatalog` |
| `Domain/Services/ProcessManager` (Terminate/SetPriority + failure classification) | Domain: `Services/ProcessOps` |
| `Domain/Services/TimerManager` | Deleted (PeriodicTimer loop in catalog) |
| `Domain/Abstractions/IDispatcherService` | Moved to app (`TaskManager.Abstractions`); implemented by existing `WpfDispatcherService`; consumed only by the catalog |
| `TaskManager/Services/Factories/*` (3 factories) | Deleted (composition root + `IWindowService`) |
| `ViewModels/Abstraction/ViewModelBase.GetAssociatedWindow` | Deleted (`IWindowService`) |
| `TaskManager/Services/FolderSelector` | Replaced by `IFolderPicker` |
| `Preconditions` flags enum | Deleted (plain guard method in MainWindowViewModel) |

## Threading Model (unchanged semantics)

- `_items` touched only on UI thread (dispatcher batch apply).
- `_index`/enrichment cache guarded by lock; workers read under lock.
- Refresh serialization via `SemaphoreSlim`: polling skips when busy; manual waits; user-initiated refresh restarts polling timer afterwards.
- Priority writeback after successful ops happens on UI thread as today.

## Testing Impact

- Every VM constructible with fakes (no Windows, no container): MainWindow, Settings, SetPriority, DataExport suites become fully headless.
- Catalog pipeline testable by faking snapshot source + dispatcher service; diff/enrich already covered by pure tests.
- `ProcessOps` keeps integration coverage unchanged (live OS state).
- Existing unit tests for settings/export VMs survive nearly unchanged since their dependencies become interfaces.

## Migration Order

1. Fix F5 (settings registration) and trivial hygiene items (dead code, duplicate usings).
2. Introduce `ProcessOps` in Domain; move kill/priority + classification; rewire callers.
3. Create `ProcessListCatalog` in app; dissolve `ProcessManager`; delete `TimerManager`; port polling to PeriodicTimer loop; move `IDispatcherService` from Domain abstractions into app (`TaskManager.Abstractions`), rewiring `WpfDispatcherService` and the catalog. Per-field INPC on `Process` stays as decided (see §1) so DataGrid cell updates keep working.
4. Introduce `IWindowService` + composition-root implementations; delete factories; remove `IServiceProvider` from all VMs; delete `GetAssociatedWindow`; wire `InitializeAsync`.
5. Dedup partial-failure helper; delete `Preconditions`; introduce `IFolderPicker`.
6. Update unit tests (fakes instead of containers/windows); run full hermetic suite; run integration suite explicitly; clean build.

## Risks / Notes

- INPC on `Process` is retained by decision (§1): grid in-place updates depend on it, and it introduces no WPF dependency into Domain. The enforced boundary is: no presentation/WPF-coupled *services or types* (dispatcher, collections) in Domain.
- `App.Restart()` on language change stays in the main VM's OpenSettings flow; `ShowSettings()` is synchronous-until-closed so the before/after comparison works unchanged.
- Fire-and-forget initial load is replaced by awaited initialization started once from the composition root; failures route through `IErrorHandler.GuardAsync` as today.

## Non-Goals

- No new features; no visual/UX changes; no exporter format changes; no CI work; no dependency additions beyond BCL (`PeriodicTimer`).
