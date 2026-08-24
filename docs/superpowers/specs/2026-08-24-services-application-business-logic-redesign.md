# Services / Application / Business Logic Redesign

**Date:** 2026-08-24
**Status:** Design proposed

## 1. Problem

The codebase has a solid foundation (Clean Architecture, result types, DI, settings pipeline) but several structural issues undermine testability, maintainability, and correctness:

**F1 — ProcessManager is a god-service; "Domain" isn't a domain.**
`ProcessManager` owns five responsibilities: observable state, PID index, enrichment cache, refresh pipeline, OS operations (kill/set-priority), polling orchestration, and UI-thread marshaling via `IDispatcherService`. The Domain project references `IDispatcherService` (a WPF abstraction) and `ObservableCollection` — presentation concepts leaking into the core.

**F2 — Service locator everywhere.**
`MainWindowViewModel`, `DataExportWindowViewModel`, and all three factories take raw `IServiceProvider` and call `GetRequiredService` at runtime. Dependencies are invisible in signatures and untestable without building a container.

**F3 — ViewModels own Windows.**
VMs resolve windows from DI, set DataContext, call `ShowDialog()`, and close themselves via `ViewModelBase.GetAssociatedWindow<T>()` — which scans `App.Current.Windows` matching `DataContext == this`. Fragile global-state lookup; VMs cannot be tested headless.

**F4 — Startup side effects in a VM constructor.**
`MainWindowViewModel`'s constructor fire-and-forgets process loading and starts polling. Constructors with async side effects are untestable and order-dependent.

**F5 — Duplicate DI registration.**
`SettingsService` registered as both `ISettingsService` (singleton) and bare concrete (singleton) — two instances, divergent state.

**F6 — Anemic/over-built pieces.**
`TimerManager` wraps `System.Timers.Timer` adding nothing; `FolderSelector` inherits `ObservableObject`; `Preconditions` flags enum guards one flag; three factories are pure `IServiceProvider` wrappers; `ReportPartialFailures` duplicated in two VMs; single-case switch in `TryExport`.

Additional findings from audit:
- `XmlExporter.PerformExport<T>` reflects on `string` (wrong type) — XML export is broken.
- `LanguageDictionary.KeysList` allocates a new dictionary per call.
- `ProcessManager.RunRefreshCoreAsync` copies entire index under lock unnecessarily.
- Commented-out dead code in `App.xaml.cs` and `MainWindowViewModel`.

## 2. Goals

1. Domain is genuinely UI-free: no `IDispatcherService`, no `ObservableCollection`, no WPF types. `INotifyPropertyChanged` on `Process` stays (it's BCL, not WPF).
2. Every ViewModel is constructible with fakes — no Windows, no container, no global state lookups.
3. No side effects in constructors — startup orchestration is explicit and testable.
4. Single responsibility per class — ProcessManager splits into state management and OS operations.
5. Fix all identified bugs (F5, XmlExporter type reflection, LanguageDictionary allocation).
6. Remove all dead code, duplicated logic, and unnecessary abstractions.

## 3. Architecture

### 3.1 Layer Responsibilities

```
TaskManager.Domain (UI-free core)
├── Models: Process, ProcessItem, ProcessSnapshot, AppSettings, ProcessOpSummary, ExportResult
├── Abstractions: ISystemProcessEnumerator, ISettingsService, ISettingsStore
├── Services: ProcessDiffEngine, ProcessEnricher, ProcessOperationsService
├── DataExport: CsvExporter, JsonExporter, ExcelExporter, XmlExporter, TxtExporter
├── Primitives: enums, extensions, ProcessBasePriority, LanguageDictionary
└── NO: IDispatcherService, ObservableCollection, TimerManager

TaskManager (WPF app)
├── Services: ProcessStore, SettingsService, MessageService, WpfDispatcherService
├── Services: IWindowService (impl), IErrorHandler (impl)
├── ViewModels: all VMs — no IServiceProvider, no side effects in ctor
├── UI: Views, Controls, Behaviors, Converters, Formatters, Localization
├── Infrastructure: FileLoggerProvider, JsonSettingsStore, LegacySettingsMigrator
└── Composition: App.xaml.cs (DI container, startup orchestration)
```

### 3.2 Dependency Graph

```
TaskManager.UnitTests        → TaskManager.Domain + TaskManager
TaskManager.IntegrationTests → TaskManager.Domain + TaskManager
TaskManager (exe)            → TaskManager.Domain
TaskManager.Domain           → BCL + CommunityToolkit.Mvvm + NtApiDotNet + ClosedXML
```

Domain depends on `CommunityToolkit.Mvvm` for `ObservableObject` (used by `Process`, `ProcessItem`). This is acceptable — it's a lightweight MVVM toolkit, not a UI framework. The key constraint remains: no WPF references in Domain.

## 4. Component Design

### 4.1 ProcessStore (app layer — replaces ProcessManager's state role)

**Responsibility:** Owns the observable process list, PID index, enrichment cache, and the refresh pipeline (snapshot → diff → enrich → apply). This is the single source of truth for the running-process list.

```csharp
// src/TaskManager/Services/ProcessStore.cs
public class ProcessStore : INotifyPropertyChanged
{
    private readonly ObservableCollection<ProcessItem> _items = [];
    private readonly Dictionary<int, ProcessItem> _index = [];
    private readonly Dictionary<int, ProcessEnrichment> _enrichment = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ISystemProcessEnumerator _enumerator;
    private readonly ProcessEnricher _enricher;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<ProcessStore> _logger;

    public ReadOnlyObservableCollection<ProcessItem> Items { get; }
    public int ProcessCount { get; private set; }

    // Snapshot for export — materialized on UI thread, safe to hold
    public IReadOnlyList<Process> SnapshotForExport() { ... }

    // Refresh pipeline — called by PollingLoop and manual refresh
    internal async Task RunRefreshAsync() { ... }

    // Priority writeback — called by ProcessOperationsService after success
    internal void WritebackPriority(int pid, int newPriority) { ... }
}
```

**Key decisions:**
- `RunRefreshAsync` is `internal` — called by `PollingLoop` (composition root) and manual refresh command. Not public; the store doesn't own polling.
- `WritebackPriority` exists so `ProcessOperationsService` can update state after a successful priority change without owning the collection.
- `ObservableCollection` lives here (app layer), not in Domain. Domain's `ProcessDiffEngine` works with plain dictionaries.

### 4.2 ProcessOperationsService (Domain — stateless OS operations)

**Responsibility:** Execute OS operations (terminate, set-priority) per PID. Returns result summaries. No state, no collection ownership.

```csharp
// src/TaskManager.Domain/Services/ProcessOperationsService.cs
public class ProcessOperationsService
{
    private readonly ILogger<ProcessOperationsService> _logger;

    public ProcessOpSummary TerminateProcesses(
        IReadOnlyCollection<int> pids,
        Action<int>? onSuccess = null) { ... }

    public ProcessOpSummary SetPriority(
        IReadOnlyCollection<int> pids,
        ProcessPriorityClass priority,
        Action<int>? onSuccess = null) { ... }
}
```

**Key decisions:**
- `onSuccess` callback lets the caller (ProcessStore) do priority writeback without the service knowing about the collection.
- Stateless — every call is independent. Testable with no setup.
- Exception classification (`ClassifyFailure`) stays here — it's pure logic about OS error codes.

### 4.3 PollingLoop (app layer — replaces TimerManager)

**Responsibility:** Async loop that triggers refresh at the configured interval. No timer events, no `async void`.

```csharp
// src/TaskManager/Services/PollingLoop.cs
public class PollingLoop : IDisposable
{
    private readonly ProcessStore _store;
    private readonly ISettingsService _settings;
    private CancellationTokenSource _cts = new();
    private Task? _running;

    public void Start() { ... }   // fires Task.Run(RunLoop)
    public void Stop() { ... }    // cancels, waits for current tick
    public void Restart() { ... } // Stop + Start

    private async Task RunLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency]));

        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            await _store.RunRefreshAsync(); // skip-if-in-flight handled inside
        }
    }
}
```

**Key decisions:**
- `PeriodicTimer` (BCL, .NET 6+) — cleaner than `System.Timers.Timer` + event + async void hop.
- Settings changes update the interval by stopping and restarting the loop with a new `PeriodicTimer`.
- No reentrancy guard needed in the loop — `ProcessStore.RunRefreshAsync` already handles skip/wait semantics via `SemaphoreSlim`.
- Owned by composition root, not by any ViewModel.

### 4.4 IWindowService (app layer — decouples VMs from windows)

**Responsibility:** All window lifecycle operations. VMs never touch `Window`, `DataContext`, `ShowDialog`, or `App.Current.Windows`.

```csharp
// src/TaskManager/Abstractions/IWindowService.cs
public interface IWindowService
{
    bool ShowSettings();
    bool ShowExport(IReadOnlyList<Process> processes);
    bool ShowSetPriority(IReadOnlyCollection<int> pids);
    string? SelectFolder();
    bool Confirm(string message, string title);
    void Alert(string message, string title);
}
```

```csharp
// src/TaskManager/Services/WindowService.cs
internal sealed class WindowService : IWindowService
{
    private readonly IServiceProvider _sp;

    public bool ShowSettings()
    {
        var vm = _sp.GetRequiredService<SettingsWindowViewModel>();
        var window = new SettingsWindow { DataContext = vm };
        return window.ShowDialog() == true;
    }

    public bool ShowExport(IReadOnlyList<Process> processes)
    {
        var vm = _sp.GetRequiredService<DataExportWindowViewModel>();
        vm.Initialize(processes);  // explicit, not constructor side-effect
        var window = new DataExportWindow { DataContext = vm };
        return window.ShowDialog() == true;
    }

    public bool ShowSetPriority(IReadOnlyCollection<int> pids)
    {
        var vm = _sp.GetRequiredService<SetPriorityWindowViewModel>();
        vm.Initialize(pids);  // explicit, not constructor side-effect
        var window = new SetPriorityWindow { DataContext = vm };
        return window.ShowDialog() == true;
    }

    public string? SelectFolder()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool Confirm(string message, string title) =>
        _sp.GetRequiredService<IMessageService>()
           .ShowMessage(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
           == MessageBoxResult.OK;

    public void Alert(string message, string title) =>
        _sp.GetRequiredService<IMessageService>()
           .ShowMessage(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
```

**Key decisions:**
- `IWindowService` lives in the WPF project (it references `Window`, `MessageBox`). It is NOT a Domain abstraction — it's a UI service.
- Window construction happens here, not in factories. Factories are deleted.
- `Initialize()` on VMs replaces constructor side-effects. VMs are plain objects until `Initialize` is called.
- `GetAssociatedWindow<T>()` on `ViewModelBase` is deleted.

### 4.5 ViewModel Changes

**MainWindowViewModel** — loses `IServiceProvider`, `IDispatcherService`, `IMessageService`, window resolution, factory resolution:

```csharp
internal class MainWindowViewModel : ViewModelBase
{
    private readonly ProcessStore _store;
    private readonly IWindowService _windowService;
    private readonly IErrorHandler _errorHandler;
    private readonly ProcessOperationsService _processOps;

    public ReadOnlyObservableCollection<ProcessItem> Processes => _store.Items;
    public int ProcessCount => _processCount; // forwarded via PropertyChanged

    // Commands — no IServiceProvider, no factories, no IMessageService
    public ICommand ExportCommand { get; }
    public ICommand TerminateCommand { get; }
    public ICommand SetPriorityCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand RefreshCommand { get; }

    // No constructor side effects. Startup called explicitly:
    public async Task InitializeAsync()
    {
        await _store.RunRefreshAsync();
    }
}
```

**DataExportWindowViewModel** — loses `IServiceProvider`, `FolderSelector`, `IMessageService`:

```csharp
internal class DataExportWindowViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IErrorHandler _errorHandler;
    private readonly IDataExporterFactory _exporterFactory;
    private readonly IWindowService _windowService;
    private IReadOnlyList<Process> _processes = [];

    public void Initialize(IReadOnlyList<Process> processes)
    {
        _processes = processes;
    }

    public string DirPath { get; set => SetProperty(ref field, value); } = string.Empty;
    public ICommand SelectFolderCommand { get; }  // calls _windowService.SelectFolder()
    public ICommand OnConfirmClick { get; }
}
```

**SetPriorityWindowViewModel** — loses `IServiceProvider`, `IMessageService`:

```csharp
internal class SetPriorityWindowViewModel : ViewModelBase
{
    private readonly ProcessOperationsService _processOps;
    private readonly ProcessStore _store;
    private readonly IWindowService _windowService;
    private readonly IErrorHandler _errorHandler;
    private IReadOnlyCollection<int> _processIds = [];

    public void Initialize(IReadOnlyCollection<int> processIds)
    {
        _processIds = processIds;
    }
}
```

**ViewModelBase** — stripped to minimum:

```csharp
internal class ViewModelBase : ObservableObject
{
    // No GetAssociatedWindow<T>() — deleted
}
```

### 4.6 Composition Root (App.xaml.cs)

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // Logging
    services.AddLogging(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(new FileLoggerProvider()); });

    // Domain services (stateless)
    services.AddSingleton<ISystemProcessEnumerator, NtSystemProcessEnumerator>();
    services.AddSingleton<ProcessEnricher>();
    services.AddSingleton<ProcessOperationsService>();

    // App services
    services.AddSingleton<ISettingsStore, JsonSettingsStore>();
    services.AddSingleton<ISettingsService, SettingsService>();  // ONE registration only
    services.AddSingleton<IMessageService, MessageService>();
    services.AddSingleton<IDispatcherService, WpfDispatcherService>();
    services.AddSingleton<IWindowService, WindowService>();
    services.AddSingleton<IErrorHandler, UiErrorHandler>();

    // Process management
    services.AddSingleton<ProcessStore>();
    services.AddSingleton<PollingLoop>();

    // Export
    services.AddSingleton<IDataExporterFactory, DataExporterFactory>();

    // ViewModels
    services.AddSingleton<MainWindowViewModel>();
    services.AddTransient<SettingsWindowViewModel>();
    services.AddTransient<DataExportWindowViewModel>();
    services.AddTransient<SetPriorityWindowViewModel>();

    // Views
    services.AddSingleton<MainWindow>();
}

private void LaunchGUI()
{
    var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
    var store = _serviceProvider.GetRequiredService<ProcessStore>();
    var polling = _serviceProvider.GetRequiredService<PollingLoop>();

    // Explicit startup — no constructor side effects
    mainWindow.Show();
    _ = Task.Run(async () =>
    {
        await store.RunRefreshAsync();
        polling.Start();
    });
}
```

### 4.7 Data Export Pipeline Fix

Replace string-round-trip with direct object serialization:

```csharp
// src/TaskManager.Domain/Services/DataExport/BaseDataExporter.cs
public abstract class BaseDataExporter
{
    protected abstract string Extension { get; }
    protected abstract void WriteFile<T>(string path, IReadOnlyList<T> records) where T : IExportable;

    public ExportResult Export<T>(string dirPath, IReadOnlyList<T> records) where T : IExportable
    {
        try
        {
            string path = dirPath + GenerateFileName(Extension);
            WriteFile(path, records);
            return ExportResult.Success(path);
        }
        catch (Exception ex) when (TryClassifyFailure(ex, out var reason))
        {
            return ExportResult.Fail(reason);
        }
    }
}
```

```csharp
// CsvExporter / TxtExporter — use ToDelimitedString (natural for text)
protected override void WriteFile<T>(string path, IReadOnlyList<T> records)
{
    File.WriteAllLines(path, records.Select(r => r.ToDelimitedString(Separator)));
}

// JsonExporter — serialize objects directly, no string intermediate
protected override void WriteFile<T>(string path, IReadOnlyList<T> records)
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(path, JsonSerializer.Serialize(records, options));
}

// ExcelExporter — reflect on T directly
protected override void WriteFile<T>(string path, IReadOnlyList<T> records)
{
    var workbook = new XLWorkbook();
    var worksheet = workbook.Worksheets.Add("Records");
    var headers = GetColumnHeaders(typeof(T));
    // ... write headers and rows from records, not from strings
    workbook.SaveAs(path);
}

// XmlExporter — reflect on T directly (fixes the bug)
protected override void WriteFile<T>(string path, IReadOnlyList<T> records)
{
    var root = new XElement("Records",
        records.Select(r => new XElement("Record",
            typeof(T).GetProperties()
                .Where(p => !p.IsDefined(typeof(IgnoreSerialization), false))
                .Select(p => new XElement(p.Name, p.GetValue(r)?.ToString() ?? "")))));
    new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(path);
}
```

### 4.8 Remaining Fixes

**IDataExporterFactory interface (Domain abstraction):**
Move `DataExporterFactory.CreateDataExporter` behind an interface in Domain abstractions. The factory stays in the app layer (it creates concrete exporters that depend on `ISettingsService`), but the ViewModel depends only on the interface.

```csharp
// src/TaskManager.Domain/Abstractions/IDataExporterFactory.cs
public interface IDataExporterFactory
{
    BaseDataExporter Create(DataType dataType);
}
```

**LanguageDictionary — static KeysList:**
```csharp
public static IReadOnlyList<string> KeysList { get; } =
    new LanguageDictionary().Keys.ToList().AsReadOnly();
```

**IErrorHandler — move to Domain abstractions:**
Move `IErrorHandler` from `TaskManager.Services.ErrorHandling` to `TaskManager.Domain.Abstractions`. The interface is UI-agnostic (takes `Exception` + `string`). `UiErrorHandler` stays in the app layer.

**Models — use ObservableObject:**
`Process` and `ProcessItem` switch from hand-rolled INPC to `ObservableObject` from CommunityToolkit.Mvvm. Reduces boilerplate by ~40 lines.

**Delete dead code:**
- `Preconditions` flags enum (replace with plain guard methods)
- Commented-out `SetPriorityWindow` registration in `App.xaml.cs`
- Commented-out `GotConfirmation` block in `MainWindowViewModel`
- `FolderSelector` class (folder picking inlined in `WindowService.SelectFolder()`)
- `SetPriorityVVmFactory`, `DataExportViewModelFactory` (window-building factories replaced by `IWindowService`)

**Duplicate using statement** in `App.xaml.cs` — removed.

## 5. What Does NOT Change

- `ProcessDiffEngine.Compute` — already pure, already correct
- `ProcessEnricher` — already stateless, already well-designed
- `NtSystemProcessEnumerator` — already minimal, already correct
- Settings pipeline (`AppSettings` → `ISettingsService` → `JsonSettingsStore`) — already approved and implemented
- Error handling tiers (Tier 1/2/3) — already approved and implemented
- Exporter family behavior — only the pipeline changes, not the output
- CommunityToolkit source-generated MVVM — stays
- Test project split (Unit + Integration) — stays
- Localization via .resx — stays

## 6. Migration Order

Each step leaves the app shippable:

1. **Bug fixes** (trivial, no structural changes):
   - Fix `LanguageDictionary.KeysList` allocation
   - Fix duplicate `SettingsService` DI registration
   - Fix `XmlExporter` type reflection bug
   - Remove duplicate `using` in `App.xaml.cs`
   - Remove commented-out dead code

2. **Extract `ProcessOperationsService`** from `ProcessManager`:
   - Create `ProcessOperationsService` in Domain
   - Move `TerminateProcesses`, `SetPriority`, `ExecutePerPid`, `ClassifyFailure` out of `ProcessManager`
   - `ProcessManager` gains `WritebackPriority` for post-success update (this becomes `ProcessStore.WritebackPriority` in step 5)
   - Update `MainWindowViewModel` and `SetPriorityWindowViewModel` to use new service
   - Update DI registration
   - Update tests

3. **Move `IErrorHandler` to Domain abstractions**:
   - Move interface to `TaskManager.Domain.Abstractions`
   - Update namespace in all consumers
   - `UiErrorHandler` stays in app layer

4. **Models → `ObservableObject`**:
   - `Process` extends `ObservableObject`, uses `[ObservableProperty]` source generator
   - `ProcessItem` extends `ObservableObject`
   - Delete hand-rolled `SetField`, `Notify`, `OnPropertyChanged` methods

5. **Introduce `IWindowService` and kill window factories**:
   - Create `IWindowService` interface and `WindowService` implementation
   - Delete `SetPriorityVVmFactory`, `DataExportViewModelFactory` (window-building factories)
   - Extract `IDataExporterFactory` interface from `DataExporterFactory` (rename class to implement it)
   - Update all ViewModels to use `IWindowService` instead of `IServiceProvider` for window operations
   - Delete `ViewModelBase.GetAssociatedWindow<T>()`

6. **VM constructor hygiene**:
   - Remove `IServiceProvider` from all VM constructors
   - Remove constructor side effects from `MainWindowViewModel`
   - Add `Initialize()` / `InitializeAsync()` methods
   - Startup orchestration moves to `App.xaml.cs` composition root

7. **Replace `TimerManager` with `PollingLoop`**:
   - Create `PollingLoop` using `PeriodicTimer`
   - Delete `TimerManager`
   - Wire `PollingLoop` in composition root

8. **Fix export pipeline**:
   - Refactor `BaseDataExporter` to `WriteFile<T>` template method
   - Update all five exporters
   - Delete string-round-trip intermediate

9. **Delete `Preconditions` enum and `FolderSelector`**:
   - Replace with plain guard methods and inline folder picking

10. **Final cleanup**:
    - Verify all tests pass
    - Verify `dotnet build` clean
    - Remove any remaining dead code

## 7. Testing Impact

| Component | Before | After |
|-----------|--------|-------|
| `MainWindowViewModel` | Requires `IServiceProvider`, `IDispatcherService`, `ProcessManager`, `IMessageService`, `IErrorHandler`, `ISettingsService`, factories | Requires `ProcessStore`, `IWindowService`, `IErrorHandler`, `ProcessOperationsService` — all fakable |
| `DataExportWindowViewModel` | Requires `IServiceProvider`, `FolderSelector`, `IMessageService`, factories | Requires `ISettingsService`, `IErrorHandler`, `IDataExporterFactory`, `IWindowService` — all fakable |
| `SetPriorityWindowViewModel` | Requires `ProcessManager`, `IMessageService`, `IErrorHandler` | Requires `ProcessOperationsService`, `ProcessStore`, `IWindowService`, `IErrorHandler` — all fakable |
| `ProcessStore` | N/A (was ProcessManager) | Fakable via `ISystemProcessEnumerator`, `ProcessEnricher` (virtual), `IDispatcherService` |
| `ProcessOperationsService` | N/A (was inline in ProcessManager) | Stateless, fakable via `ILogger` only |
| `PollingLoop` | N/A (was TimerManager) | Fakable via `ProcessStore`, `ISettingsService` |
| Export pipeline | String round-trip, `XmlExporter` broken | Direct serialization, all exporters correct |

Every VM becomes constructible with NSubstitute fakes. No `[WpfFact]` needed for basic command tests. Integration tests target `ProcessStore` and `ProcessOperationsService` unchanged.

## 8. Risk Assessment

| Risk | Mitigation |
|------|------------|
| `ObservableObject` source generator requires partial class | `Process` and `ProcessItem` become `partial` — trivial change |
| `PeriodicTimer` is .NET 6+ | Already on .NET 10 — no issue |
| `IWindowService` creates coupling between VMs and app | Interface is in app layer, not Domain; VMs depend on abstraction, not concrete |
| Moving `IErrorHandler` to Domain adds a dependency | Domain already depends on `Microsoft.Extensions.Logging.Abstractions`; `IErrorHandler` uses only `Exception` + `string` — no new deps |
| Export pipeline refactor breaks existing tests | Test assertions match on `ExportResult.IsSuccess` and file existence — unchanged |
