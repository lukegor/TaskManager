# Hygiene & Modernization Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land shortlist items #2–#11: localized validation message, dead-binding removal, responsive async process operations, modular composition root, centralized model mapping, typed exporter log categories, deterministic polling tests via `FakeTimeProvider`, WindowService STA tests, legacy-settings retirement, and test-folder casing fix.

**Architecture:** Catalog operations become `Task`-returning with writeback deliberately *after* the await so INPC mutations stay on the UI thread. Field mapping moves onto `Domain.Models.Process` (`FromSnapshot`/`ApplySnapshot`/`DeepCopy`). Composition splits into two registration extensions. Polling gets constructor-injected `TimeProvider` so tests drive ticks deterministically with `FakeTimeProvider`.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm (`AsyncRelayCommand`), Microsoft.Extensions.DependencyInjection 10.x keyed-capable, `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider), xunit.v3 + Shouldly + NSubstitute + Xunit.StaFact.

## Global Constraints

- Build: `dotnet build TaskManager.slnx`; hermetic suite: `dotnet test tests/TaskManager.UnitTests`; live suite: `dotnet test tests/TaskManager.IntegrationTests`.
- Localized key pair (exact values): name `SelectOptionsRequired`; en value `You need to select options`; pl value `Musisz wybrać opcje`.
- **No `ConfigureAwait(false)` before priority writeback** — the continuation must resume on the caller's (UI) context; this is load-bearing, do not "optimize" it.
- Sync `TerminateProcesses`/`SetPriority` on `IProcessListCatalog` are deleted, not kept alongside async.
- `Func<DataType, BaseDataExporter>` delegate stays; exporters are NOT migrated to keyed DI.
- `PeriodicTimer(TimeSpan, TimeProvider)` overload requires .NET 9+ — satisfied (repo pins .NET 10 SDK).
- New package: `Microsoft.Extensions.TimeProvider.Testing` Version `10.0.11` in `Directory.Packages.props` (central versions), referenced only by `TaskManager.UnitTests.csproj`. FakeTimeProvider lives in namespace `Microsoft.Extensions.Time.Testing`.
- Commit style: `refactor:` / `feat:` / `test:` / `build:` prefixes, imperative mood.
- Strings resources require THREE edits per key: `Strings.resx`, `Strings.pl.resx`, and a hand-added property in the committed `Strings.Designer.cs` (the ResX generator is design-time-only; `dotnet build` will NOT regenerate it).

---

### Task 1: Quick wins — localized message, dead binding, folder casing

**Files:**
- Modify: `src/TaskManager/Resources/Languages/Strings.resx` (insert after the `SelectProcess` data block, ~line 198)
- Modify: `src/TaskManager/Resources/Languages/Strings.pl.resx` (insert after its `SelectProcess` block, ~line 199)
- Modify: `src/TaskManager/Resources/Languages/Strings.Designer.cs` (insert after the `SelectProcess` property, ~line 383-389)
- Modify: `src/TaskManager/ViewModels/DataExportWindowViewModel.cs:65`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (delete `MonitoringButtonIcon`)
- Rename: `tests/TaskManager.UnitTests/Viewmodels` → `ViewModels`

**Interfaces:**
- Produces: `Strings.SelectOptionsRequired` (static property, generated-style accessor) — used by DataExportWindowViewModel in this task.

- [ ] **Step 1: Add the resx entries**

In `Strings.resx`, immediately after the `SelectProcess` `</data>` tag, insert:

```xml
  <data name="SelectOptionsRequired" xml:space="preserve">
    <value>You need to select options</value>
  </data>
```

In `Strings.pl.resx`, immediately after its `SelectProcess` `</data>` tag, insert:

```xml
  <data name="SelectOptionsRequired" xml:space="preserve">
    <value>Musisz wybrać opcje</value>
  </data>
```

- [ ] **Step 2: Hand-add the Designer property**

In `Strings.Designer.cs`, immediately after the `SelectProcess` property block, insert:

```csharp
        
        /// <summary>
        ///   Looks up a localized string similar to You need to select options.
        /// </summary>
        public static string SelectOptionsRequired {
            get {
                return ResourceManager.GetString("SelectOptionsRequired", resourceCulture);
            }
        }
```

- [ ] **Step 3: Swap the hardcoded literal**

In `src/TaskManager/ViewModels/DataExportWindowViewModel.cs` (line ~65), replace:

```csharp
_messageService.ShowMessage("You need to select options", Strings.Error,
```

with:

```csharp
_messageService.ShowMessage(Strings.SelectOptionsRequired, Strings.Error,
```

- [ ] **Step 4: Delete the dead binding**

In `src/TaskManager/ViewModels/MainWindowViewModel.cs`, delete this property (inside `#region PureUI_Bindings`):

```csharp
        public ImageSource? MonitoringButtonIcon { get; set => SetProperty(ref field, value); }
```

and delete the now-orphaned `using System.Windows.Media;`.

- [ ] **Step 5: Fix test-folder casing**

Case-only renames need an intermediate hop:

```bash
git mv tests/TaskManager.UnitTests/Viewmodels tests/TaskManager.UnitTests/tmp_rename
git mv tests/TaskManager.UnitTests/tmp_rename tests/TaskManager.UnitTests/ViewModels
```

No namespace edits — test namespaces already say `TaskManager.UnitTests.ViewModels`.

- [ ] **Step 6: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. All green required (119 tests).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: localize export validation prompt; drop dead icon binding; fix test folder casing"
```

---

### Task 2: Legacy settings retirement

**Files:**
- Delete: `src/TaskManager/Infrastructure/Settings/LegacyUserConfigSettings.cs`
- Delete: `src/TaskManager/Infrastructure/Settings/LegacySettingsMigrator.cs`
- Modify: `src/TaskManager/Infrastructure/Settings/JsonSettingsStore.cs`
- Modify: `tests/TaskManager.UnitTests/Infrastructure/JsonSettingsStoreTests.cs`

**Interfaces:**
- Produces: `JsonSettingsStore` with exactly two constructors: `JsonSettingsStore(ILogger<JsonSettingsStore>)` (production, LocalAppData path) and `JsonSettingsStore(string directory, ILogger<JsonSettingsStore>)` (tests). Missing file ⇒ `AppSettings.Defaults`.

- [ ] **Step 1: Trim the store**

In `JsonSettingsStore.cs`:

Replace the two constructors plus `_legacyReader` field (lines ~22–40) with:

```csharp
        private readonly string _filePath;
        private readonly ILogger<JsonSettingsStore> _logger;

        public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaskManager"), logger)
        {
        }

        public JsonSettingsStore(string directory, ILogger<JsonSettingsStore> logger)
        {
            _filePath = Path.Combine(directory, "settings.json");
            _logger = logger;
        }
```

Replace the missing-file branch of `Load()`:

```csharp
            if (!File.Exists(_filePath))
            {
                return LoadLegacyOrDefaults();
            }
```

with:

```csharp
            if (!File.Exists(_filePath))
            {
                return AppSettings.Defaults;
            }
```

Delete the entire `LoadLegacyOrDefaults()` method.

- [ ] **Step 2: Delete legacy files**

```bash
git rm src/TaskManager/Infrastructure/Settings/LegacyUserConfigSettings.cs src/TaskManager/Infrastructure/Settings/LegacySettingsMigrator.cs
```

- [ ] **Step 3: Retire legacy tests**

In `JsonSettingsStoreTests.cs` delete three members entirely:

1. `Load_WithoutJsonFile_ImportsLegacyValuesAndPersistsThem` (lines ~131–150)
2. `Load_WhenLegacyReaderYieldsNothing_ReturnsDefaultsWithoutCreatingFile` (lines ~152–159)
3. The `[Theory] ParseFrequency_MapsNumericStringsAndRejectsGarbage` block (lines ~161–170)

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Grep confirms no survivors:

```bash
rg -n "LegacySettingsMigrator|LegacyUserConfig|_legacyReader" src tests --glob "*.cs"
# expect: no output
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: retire legacy ApplicationSettingsBase migration path"
```

---

### Task 3: Centralize model mapping on `Process`

**Files:**
- Modify: `src/TaskManager.Domain/Models/Process.cs`
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs` (three call sites, delete three private methods)
- Test (create): `tests/TaskManager.UnitTests/DomainModels/ProcessMappingTests.cs`

**Interfaces:**
- Consumes: `ProcessEnrichment` (in `TaskManager.Domain.Services`, record struct `(string Path, ArchitectureType Architecture)`), `ProcessSnapshot(int Pid, string Name, int ThreadCount, int? Ppid, int? BasePriority)`.
- Produces (used by the catalog):

```csharp
public static Process FromSnapshot(ProcessSnapshot snapshot, ProcessEnrichment enrichment);
public void ApplySnapshot(ProcessSnapshot snapshot);
public Process DeepCopy();
```

- [ ] **Step 1: Add the model members**

In `Process.cs`, add `using TaskManager.Domain.Services;` at the top and these members (place after the `ArchitectureTypeDisplay` property):

```csharp
        /// <summary>Single field-mapping point from a fresh snapshot plus enrichment.</summary>
        public static Process FromSnapshot(ProcessSnapshot snapshot, ProcessEnrichment enrichment) => new()
        {
            Name = snapshot.Name,
            Pid = snapshot.Pid,
            Path = enrichment.Path,
            ArchitectureType = enrichment.Architecture,
            Priority = snapshot.BasePriority,
            ThreadCount = snapshot.ThreadCount,
            Ppid = snapshot.Ppid,
        };

        /// <summary>In-place update from a snapshot for fields known to mutate at runtime.</summary>
        public void ApplySnapshot(ProcessSnapshot s)
        {
            Name = s.Name;
            ThreadCount = s.ThreadCount;
            Priority = s.BasePriority;
            Ppid = s.Ppid;
        }

        /// <summary>Detached copy safe to hold across refreshes (export snapshots).</summary>
        public Process DeepCopy() => new()
        {
            Name = Name,
            Pid = Pid,
            Path = Path,
            ArchitectureType = ArchitectureType,
            Priority = Priority,
            ThreadCount = ThreadCount,
            Ppid = Ppid,
        };
```

(`Path` here is the domain property, not `System.IO.Path` — `Process.cs` has no `System.IO` using, so no ambiguity.)

- [ ] **Step 2: Rewire the catalog**

In `ProcessListCatalog.cs`:

- In `ApplyBatch`, replace `var process = Materialize(added, enrichedNew[added.Pid]);` with `var process = Process.FromSnapshot(added, enrichedNew[added.Pid]);`
- Same method, replace `ApplySnapshot(item.Process, upd);` with `item.Process.ApplySnapshot(upd);`
- In `SnapshotForExport`, replace `return _index.Values.Select(i => CopyOf(i.Process)).ToList();` with `return _index.Values.Select(i => i.Process.DeepCopy()).ToList();`
- Delete the private static methods `Materialize`, `ApplySnapshot`, `CopyOf`.

- [ ] **Step 3: Add mapping tests**

Create `tests/TaskManager.UnitTests/DomainModels/ProcessMappingTests.cs`:

```csharp
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

namespace TaskManager.UnitTests.DomainModels
{
    /// <summary>
    /// Contract: one mapping point for snapshot->model, in-place runtime updates raise
    /// per-field notification, and deep copies detach completely from the source row.
    /// </summary>
    public class ProcessMappingTests
    {
        private static readonly ProcessSnapshot Snapshot =
            new(Pid: 42, Name: "alpha", ThreadCount: 7, Ppid: 4, BasePriority: 8);

        private static readonly ProcessEnrichment Enrichment =
            new(@"C:\windows\alpha.exe", ArchitectureType._64BIT);

        [Fact]
        public void FromSnapshot_MapsEveryField()
        {
            var process = Process.FromSnapshot(Snapshot, Enrichment);

            process.Pid.ShouldBe(42);
            process.Name.ShouldBe("alpha");
            process.ThreadCount.ShouldBe(7);
            process.Ppid.ShouldBe(4);
            process.Priority.ShouldBe(8);
            process.Path.ShouldBe(@"C:\windows\alpha.exe");
            process.ArchitectureType.ShouldBe(ArchitectureType._64BIT);
        }

        [Fact]
        public void ApplySnapshot_UpdatesRuntimeMutableFields_AndRaisesNotification()
        {
            var process = Process.FromSnapshot(Snapshot, Enrichment);
            var notified = new List<string>();
            process.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            process.ApplySnapshot(new ProcessSnapshot(42, "beta", ThreadCount: 9, Ppid: 4, BasePriority: 6));

            process.Name.ShouldBe("beta");
            process.ThreadCount.ShouldBe(9);
            process.Priority.ShouldBe(6);
            notified.ShouldContain(nameof(Process.Name));
            notified.ShouldContain(nameof(Process.ThreadCount));
            notified.ShouldContain(nameof(Process.Priority));
            process.Path.ShouldBe(@"C:\windows\alpha.exe"); // enrichment fields untouched
        }

        [Fact]
        public void DeepCopy_IsFullyDetached()
        {
            var original = Process.FromSnapshot(Snapshot, Enrichment);

            var copy = original.DeepCopy();
            copy.Priority = 4;
            copy.Name = "mutated";

            original.Priority.ShouldBe(8);
            original.Name.ShouldBe("alpha");
            copy.Pid.ShouldBe(original.Pid);
        }
    }
}
```

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: centralize snapshot/deep-copy field mapping on Process"
```

---

### Task 4: Split the composition root

**Files:**
- Create: `src/TaskManager/Infrastructure/Composition/CoreServicesRegistration.cs`
- Create: `src/TaskManager/Infrastructure/Composition/UiServicesRegistration.cs`
- Modify: `src/TaskManager/App.xaml.cs`

**Interfaces:**
- Produces: extension methods `AddCoreServices(this IServiceCollection)` and `AddUiServices(this IServiceCollection)`; `App.ConfigureServices` reduces to logging + those two calls. Registration CONTENT moves verbatim — no lifetime changes.

- [ ] **Step 1: Create `CoreServicesRegistration`**

Create `src/TaskManager/Infrastructure/Composition/CoreServicesRegistration.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Infrastructure.Settings;
using TaskManager.Presentation;
using TaskManager.Services;

namespace TaskManager.Infrastructure.Composition
{
    /// <summary>Registrations for core/presentation state and OS-facing services.</summary>
    internal static class CoreServicesRegistration
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<SettingsService>();
            services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<ISystemProcessEnumerator, NtSystemProcessEnumerator>();
            services.AddSingleton<ProcessEnricher>();
            services.AddSingleton<ProcessOperationsService>();
            services.AddSingleton<IProcessOperations>(sp => sp.GetRequiredService<ProcessOperationsService>());
            services.AddSingleton<ProcessListCatalog>();
            services.AddSingleton<IProcessListCatalog>(sp => sp.GetRequiredService<ProcessListCatalog>());
            return services;
        }
    }
}
```

- [ ] **Step 2: Create `UiServicesRegistration`**

Create `src/TaskManager/Infrastructure/Composition/UiServicesRegistration.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Infrastructure.Composition
{
    /// <summary>Registrations for dialogs, reporting, and view/viewmodel wiring.</summary>
    internal static class UiServicesRegistration
    {
        public static IServiceCollection AddUiServices(this IServiceCollection services)
        {
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IErrorHandler, UiErrorHandler>();
            services.AddSingleton<IFolderPicker, FolderPicker>();

            services.AddSingleton<Func<DataType, BaseDataExporter>>(sp => dataType => dataType switch
            {
                DataType.Csv => new CsvExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Txt => new TxtExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Xlsx => new ExcelExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Json => new JsonExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Xml => new XmlExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            });

            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>(sp => new MainWindow
            {
                DataContext = sp.GetRequiredService<MainWindowViewModel>()
            });
            return services;
        }
    }
}
```

(The exporter loggers stay `ILogger<BaseDataExporter>` here; Task 5 retypes them.)

- [ ] **Step 3: Slim `App.xaml.cs`**

`ConfigureServices` becomes:

```csharp
        private void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new FileLoggerProvider());
            });

            services.AddCoreServices();
            services.AddUiServices();
        }
```

Delete every moved registration line from `ConfigureServices` and prune the now-unused usings (`TaskManager.Abstractions`, `TaskManager.Domain.Abstractions`, `TaskManager.Domain.Services`, `TaskManager.Domain.Services.DataExport`, `TaskManager.Domain.Primitives`, `TaskManager.Presentation`, `TaskManager.Services`, `TaskManager.Services.ErrorHandling`, `TaskManager.UI.Views`, `TaskManager.ViewModels`) — keep only what the remaining code references (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `System.Globalization`, `System.Windows`, `TaskManager.Infrastructure.Logging`, `TaskManager.Infrastructure.Composition`). Re-add any the compiler asks for.

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Grep proves the bootstrapper is thin:

```bash
rg -n "AddSingleton|AddTransient" src/TaskManager/App.xaml.cs
# expect: no output
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: split composition root into core/ui registration extensions"
```

---

### Task 5: Typed exporter loggers

**Files:**
- Modify: `src/TaskManager.Domain/Services/DataExport/BaseDataExporter.cs`
- Modify: each of `CsvExporter.cs`, `TxtExporter.cs`, `JsonExporter.cs`, `XmlExporter.cs`, `ExcelExporter.cs` (ctor parameter type only)
- Modify: `src/TaskManager/Infrastructure/Composition/UiServicesRegistration.cs` (switch branches)
- Modify: `tests/TaskManager.UnitTests/ViewModels/DataExportWindowViewModelTests.cs` (NullLogger types)

**Interfaces:**
- Produces: `protected BaseDataExporter(ISettingsService settings, ILogger logger)`; derived ctors take their own `ILogger<TDerived>`. Log categories become concrete exporter types.

- [ ] **Step 1: Retype the base**

In `BaseDataExporter.cs`, change field and constructor (usage sites of `_logger` unchanged — non-generic `ILogger` has `LogWarning`):

```csharp
        private readonly ILogger _logger;

        public BaseDataExporter(ISettingsService settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;
        }
```

- [ ] **Step 2: Retype each derived constructor**

Change ONLY the ctor parameter type in each file (bodies identical):

```csharp
// CsvExporter.cs
public CsvExporter(ISettingsService settings, ILogger<CsvExporter> logger)
    : base(settings, logger)
```

```csharp
// TxtExporter.cs
public TxtExporter(ISettingsService settings, ILogger<TxtExporter> logger)
    : base(settings, logger)
```

```csharp
// JsonExporter.cs
public JsonExporter(ISettingsService settings, ILogger<JsonExporter> logger) : base(settings, logger)
```

```csharp
// XmlExporter.cs
public XmlExporter(ISettingsService settings, ILogger<XmlExporter> logger) : base(settings, logger)
```

```csharp
// ExcelExporter.cs
public ExcelExporter(ISettingsService settings, ILogger<ExcelExporter> logger) : base(settings, logger)
```

- [ ] **Step 3: Resolve typed loggers in composition**

In `UiServicesRegistration.cs`, replace the switch with:

```csharp
            services.AddSingleton<Func<DataType, BaseDataExporter>>(sp => dataType => dataType switch
            {
                DataType.Csv => new CsvExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<CsvExporter>>()),
                DataType.Txt => new TxtExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<TxtExporter>>()),
                DataType.Xlsx => new ExcelExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<ExcelExporter>>()),
                DataType.Json => new JsonExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<JsonExporter>>()),
                DataType.Xml => new XmlExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<XmlExporter>>()),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            });
```

- [ ] **Step 4: Update test constructors**

In `DataExportWindowViewModelTests.cs`, replace every `NullLogger<BaseDataExporter>.Instance` passed to a **derived** exporter with the derived type — specifically:

- `new TxtExporter(NewSettings(), NullLogger<TxtExporter>.Instance)` (three occurrences: `TryExport_Success`, `Constructor_HoldsMaterializedSnapshot`, and inside `ThrowingExporter`'s base call)

The `ThrowingExporter` ctor becomes:

```csharp
            public ThrowingExporter(ISettingsService settings)
                : base(settings, NullLogger<TxtExporter>.Instance)
```

The `NullLogger<UiErrorHandler>.Instance` occurrences stay as they are.

- [ ] **Step 5: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: per-format log categories for data exporters"
```

---

### Task 6: Responsive operations — async catalog + async commands

**Files:**
- Modify: `src/TaskManager/Abstractions/IProcessListCatalog.cs`
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- Modify: `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs` (3 tests)
- Modify: `tests/TaskManager.UnitTests/ViewModels/MainWindowViewModelTests.cs` (5 tests)
- Modify: `tests/TaskManager.UnitTests/ViewModels/SetPriorityWindowViewModelTests.cs` (4 tests)

**Interfaces:**
- Produces (replacing the sync members):

```csharp
Task<ProcessOpSummary> TerminateProcessesAsync(IReadOnlyCollection<int> pids);
Task<ProcessOpSummary> SetPriorityAsync(IReadOnlyCollection<int> pids, ProcessPriorityClass priority);
```

- Consumes: existing `IErrorHandler.GuardAsync(Func<Task>, [CallerMemberName] string operationContext = "")` — pass explicit context strings where the lambda is not a named method.

- [ ] **Step 1: Make catalog operations async**

In `IProcessListCatalog.cs` replace the two sync declarations with the async signatures above (doc comments carried over). In `ProcessListCatalog.cs` replace both bodies:

```csharp
        public async Task<ProcessOpSummary> TerminateProcessesAsync(IReadOnlyCollection<int> pids)
        {
            // Deliberately NO ConfigureAwait(false) anywhere in these methods:
            // SetPriority's writeback mutates INPC-bound rows and must resume on the
            // caller's context (the UI thread when invoked from commands).
            return await Task.Run(() => _processOps.TerminateProcesses(pids));
        }

        public async Task<ProcessOpSummary> SetPriorityAsync(IReadOnlyCollection<int> pids, ProcessPriorityClass priority)
        {
            var summary = await Task.Run(() => _processOps.SetPriority(pids, priority));

            foreach (var pid in summary.SucceededPids)
            {
                WritebackPriority(pid, ProcessBasePriority.Get(priority));
            }

            return summary;
        }
```

Delete the sync `TerminateProcesses`/`SetPriority` methods.

- [ ] **Step 2: Async commands in `MainWindowViewModel`**

Add `using CommunityToolkit.Mvvm.Input;` (already present) — change two command constructions in the constructor:

```csharp
            TerminateCommand = new AsyncRelayCommand(TerminateAsync);
            SetPriorityCommand = new AsyncRelayCommand(SetPriorityAsync);
```

Change the two property declarations to:

```csharp
        public AsyncRelayCommand TerminateCommand { get; }
        public AsyncRelayCommand SetPriorityCommand { get; }
```

Replace the two private methods with:

```csharp
        private async Task TerminateAsync()
        {
            if (!EnsureSelection())
            {
                return;
            }

            if (_messageService.ShowMessage(Strings.AskingForConfirmation, Strings.Confirm,
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel)
            {
                return;
            }

            await _errorHandler.GuardAsync(async () =>
            {
                var summary = await _catalog.TerminateProcessesAsync(GetSelectedPids());
                ReportPartialFailures(summary);
            }, "terminating selected processes");
        }

        private async Task SetPriorityAsync()
        {
            if (!EnsureSelection())
            {
                return;
            }

            await _errorHandler.GuardAsync(() =>
            {
                _windows.ShowSetPriority(GetSelectedPids());
                return Task.CompletedTask;
            }, "opening the set-priority dialog");
        }

        private int[] GetSelectedPids() =>
            GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray();
```

(`ExportCommand`, `OpenSettingsCommand` stay `RelayCommand`; the modal dialog pumps messages so the parent UI stays responsive.)

- [ ] **Step 3: Async confirm in `SetPriorityWindowViewModel`**

Constructor line becomes:

```csharp
            OnConfirmCommand = new AsyncRelayCommand(OnConfirmAsync);
```

Property declaration becomes:

```csharp
        public AsyncRelayCommand OnConfirmCommand { get; }
```

Replace `OnConfirm` with:

```csharp
        private async Task OnConfirmAsync()
        {
            await _errorHandler.GuardAsync(async () =>
            {
                if (Priority == null)
                {
                    _messageService.ShowMessage(Strings.Select, Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var summary = await _catalog.SetPriorityAsync(_processIds, (ProcessPriorityClass)Priority);
                ReportPartialFailures(summary);

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "applying the selected priority");
        }
```

- [ ] **Step 4: Update catalog delegation tests**

In `ProcessListCatalogTests.cs`, make the three ops tests async and await the new names. NSubstitute auto-wraps returned values for `Task<T>` members, so `.Returns(...)` shapes stay identical:

```csharp
        [Fact]
        public async Task TerminateProcessesAsync_DelegatesToOpsService()
        {
            _ops.TerminateProcesses(Arg.Any<IReadOnlyCollection<int>>())
                .Returns(ProcessOpSummary.Empty);

            await _catalog.TerminateProcessesAsync([42]);

            _ops.Received(1).TerminateProcesses(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 42));
        }

        [Fact]
        public async Task SetPriorityAsync_SuccessfulPids_AreWrittenBackIntoStoredRows()
        {
            _enumerator.Queue(Snap(9));
            await _catalog.LoadForTestAsync();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary { SucceededPids = [9], Failures = [] });

            var summary = await _catalog.SetPriorityAsync([9], ProcessPriorityClass.AboveNormal);

            summary.HasFailures.ShouldBeFalse();
            _catalog.Items.Single().Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public async Task SetPriorityAsync_FailedPids_AreNotWrittenBack()
        {
            _enumerator.Queue(Snap(9));
            await _catalog.LoadForTestAsync();

            _ops.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(new ProcessOpSummary
                {
                    SucceededPids = [],
                    Failures = [new ProcessOpFailure(9, ProcessOpFailureReason.AccessDenied)]
                });

            await _catalog.SetPriorityAsync([9], ProcessPriorityClass.BelowNormal);

            _catalog.Items.Single().Process.Priority.ShouldNotBe(6); // untouched
        }
```

- [ ] **Step 5: Await the async VM commands in tests**

In `MainWindowViewModelTests.cs` add `using CommunityToolkit.Mvvm.Input;` and convert five tests to `async Task`, replacing `vm.X.Execute(null)` with awaited execution:

```csharp
        [Fact]
        public async Task Terminate_NoSelection_ShowsAlert_AndNeverTouchesCatalogOrConfirm()
        {
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _messages.Received(1).ShowMessage(
                Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _catalog.DidNotReceive().TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public async Task Terminate_UserCancelsConfirmation_OperationSkipped()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.Cancel);
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _catalog.DidNotReceive().TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public async Task Terminate_Confirmed_CleanSummary_NoExtraMessages()
        {
            SeedRows(Row(1, selected: true), Row(2, selected: false));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>()).Returns(ProcessOpSummary.Empty);
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _catalog.Received(1).TerminateProcessesAsync(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 1)); // unselected row excluded
            _messages.ReceivedCalls().Count().ShouldBe(1); // confirmation prompt only
        }

        [Fact]
        public async Task Terminate_PartialFailures_ReportsFormattedSummary()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>()).Returns(new ProcessOpSummary
            {
                SucceededPids = [],
                Failures = [new ProcessOpFailure(1, ProcessOpFailureReason.AccessDenied)]
            });
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _messages.Received(1).ShowMessage(
                string.Format(Strings.OpsCompletedWithFailuresFormat, 0, 1),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [Fact]
        public async Task SetPriority_Selected_DelegatesSelectedPidsToWindowService()
        {
            SeedRows(Row(5, selected: true), Row(6, selected: true));
            var vm = CreateViewModel();

            await vm.SetPriorityCommand.ExecuteAsync(null);

            _windows.Received(1).ShowSetPriority(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.OrderBy(x => x).SequenceEqual(new[] { 5, 6 })));
        }
```

`SetPriority_NoSelection_Alerts_AndDoesNotOpenDialog` converts identically (`async Task`, `await vm.SetPriorityCommand.ExecuteAsync(null);`, `_windows.DidNotReceive().ShowSetPriority(...)`).

`AsyncRelayCommand` exposes `ExecuteAsync(object?)` directly — no cast needed.

In `SetPriorityWindowViewModelTests.cs`, convert all four `Confirm_*` tests to `async Task` with `await vm.OnConfirmCommand.ExecuteAsync(null);` in place of `vm.OnConfirmCommand.Execute(null);` — assertions otherwise unchanged.

- [ ] **Step 6: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: async process operations keep the UI thread free during batch kills"
```

---

### Task 7: Deterministic polling — TimeProvider + polling tests

**Files:**
- Create: `tests/TaskManager.UnitTests/TestSupport/ProcessFakes.cs`
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs` (ctor + timer construction)
- Modify: `src/TaskManager/Abstractions` — none (DI-only concern)
- Modify: `src/TaskManager/Infrastructure/Composition/CoreServicesRegistration.cs` (one line)
- Modify: `Directory.Packages.props` (one line)
- Modify: `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (one reference)
- Modify: `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogTests.cs` (consume shared fakes, new ctor arg)
- Test (create): `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogPollingTests.cs`

**Interfaces:**
- Produces: `ProcessListCatalog` ctor gains `TimeProvider timeProvider` between `processOps` and `logger`:

```csharp
public ProcessListCatalog(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
    ProcessEnricher enricher, ISettingsService settings, IProcessOperations processOps,
    TimeProvider timeProvider, ILogger<ProcessListCatalog> logger)
```

Shared test fakes (namespace `TaskManager.UnitTests.TestSupport`): `ScriptedEnumerator`, `CountingEnricher`, `ProcessFakes.Snap/SelfSnap`, `PollingTestHelper.WaitForCaptureCountAsync`.

- [ ] **Step 1: Add the testing package**

In `Directory.Packages.props`, inside the tests `PackageVersion` group, add:

```xml
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.11" />
```

In `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj`, add to the test-package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

- [ ] **Step 2: Extract shared fakes**

Create `tests/TaskManager.UnitTests/TestSupport/ProcessFakes.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

namespace TaskManager.UnitTests.TestSupport
{
    /// <summary>Queue-driven enumerator: each Capture consumes the next scripted result.</summary>
    internal sealed class ScriptedEnumerator : ISystemProcessEnumerator
    {
        private readonly Queue<Func<IReadOnlyList<ProcessSnapshot>>> _scripts = new();
        public int CallCount { get; private set; }
        private Exception? _throwOnce;

        public static ScriptedEnumerator Of(params ProcessSnapshot[] items)
        {
            var e = new ScriptedEnumerator();
            e.Queue(items);
            return e;
        }

        public void Queue(params ProcessSnapshot[] items) => Queue(() => items);

        public void Queue(Func<IReadOnlyList<ProcessSnapshot>> script) => _scripts.Enqueue(script);

        public void ThrowNext(Exception ex) => _throwOnce = ex;

        public IReadOnlyList<ProcessSnapshot> Capture()
        {
            CallCount++;
            if (_throwOnce is not null)
            {
                var ex = _throwOnce;
                _throwOnce = null;
                throw ex;
            }

            return _scripts.Dequeue()();
        }
    }

    /// <summary>Enricher that never touches the OS and counts probes.</summary>
    internal sealed class CountingEnricher : ProcessEnricher
    {
        public int Calls { get; private set; }

        public CountingEnricher() : base(NullLogger<ProcessEnricher>.Instance)
        {
        }

        public override bool TryEnrich(int pid, out ProcessEnrichment enrichment)
        {
            Calls++;
            enrichment = new ProcessEnrichment($@"C:\fake-{pid}-{Calls}.exe", ArchitectureType._64BIT);
            return true;
        }
    }

    internal static class ProcessFakes
    {
        public static ProcessSnapshot Snap(int pid, string name = "n", int threadCount = 1) =>
            new(pid, name, ThreadCount: threadCount, Ppid: 4, BasePriority: 8);

        public static ProcessSnapshot SelfSnap() =>
            new(Environment.ProcessId, "self", ThreadCount: 1, Ppid: null, BasePriority: 8);
    }

    internal static class PollingTestHelper
    {
        /// <summary>
        /// Awaits until the enumerator has been entered N times. Happy path is instant;
        /// the real-time deadline is purely a failure backstop against lost ticks.
        /// </summary>
        public static async Task WaitForCaptureCountAsync(ScriptedEnumerator enumerator, int expected)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (enumerator.CallCount < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            enumerator.CallCount.ShouldBeGreaterThanOrEqualTo(expected);
        }
    }
}
```

- [ ] **Step 3: Inject TimeProvider into the catalog**

In `ProcessListCatalog.cs`:

- Add field `private readonly TimeProvider _timeProvider;`
- Extend the constructor per the signature above and assign it.
- In `RunPollingLoopAsync`, replace the timer construction:

```csharp
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), _timeProvider);
```

- [ ] **Step 4: Register and repoint consumers**

In `CoreServicesRegistration.AddCoreServices`, add:

```csharp
            services.AddSingleton(TimeProvider.System);
```

In `ProcessListCatalogTests.cs`: delete the nested `ScriptedEnumerator`, `CountingEnricher`, `Snap`, `SelfSnap` members; add `using Microsoft.Extensions.Time.Testing; using TaskManager.UnitTests.TestSupport;`; qualify every former nested usage with `ProcessFakes.Snap(...)` / `ProcessFakes.SelfSnap()`; and pass `new FakeTimeProvider()` as the sixth argument in all three catalog constructions (main fixture ctor, `RemovedPid_ReEnrichedOnReuse_EvictsCacheEntry`, `DispatcherFailure_IsSwallowedByPollingWrapper`). Example main construction:

```csharp
            _catalog = new ProcessListCatalog(
                _dispatcher,
                _enumerator,
                new CountingEnricher(),
                _settings,
                _ops,
                new FakeTimeProvider(),
                NullLogger<ProcessListCatalog>.Instance);
```

- [ ] **Step 5: Create the polling test suite**

Create `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogPollingTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;
using TaskManager.Presentation;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Presentation
{
    /// <summary>
    /// Polling-loop dynamics driven by a FakeTimeProvider: ticks occur only when the fake
    /// clock advances, so interval changes, pause/resume, and phase restarts are verified
    /// without real delays.
    /// </summary>
    public class ProcessListCatalogPollingTests
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly IProcessOperations _ops = Substitute.For<IProcessOperations>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly FakeTimeProvider _time = new();

        private ProcessListCatalog CreateCatalog(Action<AppSettings>? currentOverride = null)
        {
            var current = AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.Low };
            if (currentOverride is not null)
            {
                currentOverride(current);
            }

            _settings.Current.Returns(current);

            return new ProcessListCatalog(
                InlineDispatcher, _enumerator, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);
        }

        private void SettingsChangedTo(AppSettings settings)
        {
            _settings.Current.Returns(settings);                          // service swaps snapshot first
            _settings.Changed += Raise.Event<Action<AppSettings>>(settings); // then notifies consumers
        }

        [Fact]
        public async Task FirstTick_HappensExactlyAfterOneInterval()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));

            await catalog.InitializeAsync();
            _enumerator.CallCount.ShouldBe(1); // initial fill only

            _time.Advance(TimeSpan.FromSeconds(9)); // below the Low interval
            _enumerator.CallCount.ShouldBe(1);

            _time.Advance(TimeSpan.FromSeconds(1)); // crosses 10s
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);
        }

        [Fact]
        public async Task IntervalChange_TakesEffectOnNextPeriod()
        {
            var catalog = CreateCatalog(); // Low = 10s
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            SettingsChangedTo(AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.High }); // 5s

            _time.Advance(TimeSpan.FromSeconds(5)); // new period; old 10s period not yet due
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2);

            _time.Advance(TimeSpan.FromSeconds(5));
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 3);
        }

        [Fact]
        public async Task Paused_StopsTicking_ResumeStartsFreshLoop()
        {
            var catalog = CreateCatalog(); // Low
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            SettingsChangedTo(AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.Paused });

            _time.Advance(TimeSpan.FromMinutes(1));
            _enumerator.CallCount.ShouldBe(1); // paused: idle

            _enumerator.Queue(ProcessFakes.Snap(1));
            SettingsChangedTo(AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.Low });

            _time.Advance(TimeSpan.FromSeconds(10));
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2); // fresh loop ticking again
        }

        [Fact]
        public async Task ManualRefresh_RestartsPollingPhase()
        {
            var catalog = CreateCatalog(); // Low = 10s
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            _time.Advance(TimeSpan.FromSeconds(8)); // near the end of the first period
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.PerformRefreshAsync(isUserInitiated: true); // capture #2 + phase restart

            _time.Advance(TimeSpan.FromSeconds(9)); // would have ticked at t=10 pre-restart
            _enumerator.CallCount.ShouldBe(2);

            _time.Advance(TimeSpan.FromSeconds(1)); // 10s after the manual completion
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 3);
        }

        [Fact]
        public async Task TickDuringInFlightRefresh_IsSkipped_NotQueued()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.InitializeAsync();

            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);

            _time.Advance(TimeSpan.FromSeconds(10)); // loop begins capture and blocks inside it
            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2); // CallCount counts entries

            _time.Advance(TimeSpan.FromSeconds(10)); // second tick must skip: gate held
            releaseFirst.SetResult();

            await PollingTestHelper.WaitForCaptureCountAsync(_enumerator, 2); // no third capture appeared
            await Task.Delay(50);
            _enumerator.CallCount.ShouldBe(2);
        }

        private static readonly IDispatcherService InlineDispatcher = MakeInlineDispatcher();

        private static IDispatcherService MakeInlineDispatcher()
        {
            var d = Substitute.For<IDispatcherService>();
            d.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ((Action)ci[0])());
            return d;
        }
    }
}
```

- [ ] **Step 6: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests` twice consecutively (polling tests must be stable across runs).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: inject TimeProvider into polling; deterministic FakeTimeProvider loop tests"
```

---

### Task 8: WindowService factories + STA wiring tests

**Files:**
- Modify: `src/TaskManager/Services/WindowService.cs`
- Test (create): `tests/TaskManager.UnitTests/Services/WindowServiceTests.cs`

**Interfaces:**
- Produces (all `internal`, on `WindowService`):

```csharp
internal (SettingsWindow Window, SettingsWindowViewModel ViewModel) CreateSettingsDialog();
internal (DataExportWindow Window, DataExportWindowViewModel ViewModel) CreateExportDialog(IReadOnlyList<Process> processes);
internal (SetPriorityWindow Window, SetPriorityWindowViewModel ViewModel) CreatePriorityDialog(IReadOnlyCollection<int> pids);
```

Public `Show*` methods build via factories and only add `ShowDialog()`.

- [ ] **Step 1: Split factories from showing**

Rewrite `WindowService.cs` body:

```csharp
        public void ShowSettings()
        {
            var (window, viewModel) = CreateSettingsDialog();
            ShowDialogWithCloseRelay(window, viewModel);
        }

        public void ShowExport(IReadOnlyList<Process> processes)
        {
            var (window, viewModel) = CreateExportDialog(processes);
            ShowDialogWithCloseRelay(window, viewModel);
        }

        public bool ShowSetPriority(IReadOnlyCollection<int> pids)
        {
            var (window, viewModel) = CreatePriorityDialog(pids);
            ShowDialogWithCloseRelay(window, viewModel);
            return viewModel.Confirmed;
        }

        internal (SettingsWindow Window, SettingsWindowViewModel ViewModel) CreateSettingsDialog() =>
            (new SettingsWindow(), new SettingsWindowViewModel(_settings, _errorHandler));

        internal (DataExportWindow Window, DataExportWindowViewModel ViewModel) CreateExportDialog(
            IReadOnlyList<Process> processes) =>
            (new DataExportWindow(),
             new DataExportWindowViewModel(_messages, _errorHandler, _exporterFactory, _folderPicker, processes));

        internal (SetPriorityWindow Window, SetPriorityWindowViewModel ViewModel) CreatePriorityDialog(
            IReadOnlyCollection<int> pids) =>
            (new SetPriorityWindow(), new SetPriorityWindowViewModel(_messages, _catalog, pids, _errorHandler));

        private static void ShowDialogWithCloseRelay(System.Windows.Window window, IRequestCloseObservable viewModel)
        {
            window.DataContext = viewModel;
            viewModel.RequestClose += (_, _) => window.Close();
            window.ShowDialog();
        }
```

- [ ] **Step 2: Create STA wiring tests**

Create `tests/TaskManager.UnitTests/Services/WindowServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics;
using System.IO;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;
using DataTypeEnum = TaskManager.Domain.Primitives.DataType;

namespace TaskManager.UnitTests.Services
{
    /// <summary>
    /// Dialog wiring contract: factories produce correctly paired window+viewmodel, and the
    /// close relay translates a ViewModel RequestClose into Window.Closed. Windows are never
    /// shown (WPF raises Closed even for a never-shown window being closed).
    /// </summary>
    public class WindowServiceTests
    {
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IFolderPicker _folderPicker = Substitute.For<IFolderPicker>();

        private WindowService CreateService() => new(
            _settings,
            _errorHandler,
            _messages,
            _catalog,
            dataType => new TxtExporter(NewSettings(), NullLogger<TxtExporter>.Instance),
            _folderPicker);

        private static ISettingsService NewSettings()
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Defaults);
            return settings;
        }

        [WpfFact]
        public void CreateSettingsDialog_PairsWindowWithSettingsViewModel()
        {
            var (window, viewModel) = CreateService().CreateSettingsDialog();

            window.ShouldNotBeNull();
            window.DataContext.ShouldBe(viewModel);
            viewModel.ShouldNotBeNull();
        }

        [WpfFact]
        public void CreateExportDialog_SeedsWithMaterializedProcesses()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tm-winsvc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var processes = new List<Process>
                {
                    new() { Name = "p1", Pid = 1, Path = string.Empty },
                };
                var (window, viewModel) = CreateService().CreateExportDialog(processes);

                window.DataContext.ShouldBe(viewModel);
                viewModel.DirPath = directory;

                processes.Clear(); // caller-side mutation must not leak into the dialog

                viewModel.TryExport(DataTypeEnum.Txt).ShouldBeTrue();

                var written = File.ReadAllLines(Directory.GetFiles(directory, "record-*").Single());
                written.Count(line => line.Contains("p1")).ShouldBe(1); // snapshot survived the Clear()
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [WpfFact]
        public void CreatePriorityDialog_RequestClose_RaisesWindowClosed_AndMarksConfirmed()
        {
            var (window, viewModel) = CreateService().CreatePriorityDialog([1, 2]);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            _catalog.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(ProcessOpSummary.Empty);
            viewModel.Priority = ProcessPriorityClass.Normal;

            viewModel.OnConfirmCommand.ExecuteAsync(null);
            closed.ShouldBeTrue();                       // relay translated RequestClose -> Close
            viewModel.Confirmed.ShouldBeTrue();          // ShowSetPriority will surface this
            window.DataContext.ShouldBe(viewModel);
        }
    }
}
```

Notes for the implementer:

- `viewModel.OnConfirmCommand.ExecuteAsync(null)` returns a `Task`; xunit.v3 `WpfFact` supports `async Task` test methods. If you prefer strictness, declare the last test `async Task` and `await` it — both compile.
- The export test proves the ViewModel materialized its own copy of the process list (the caller's `Clear()` after handoff cannot remove "p1" from the written file) — the exact contract `DataExportWindowViewModelTests.Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations` already establishes at unit level, now re-proven through the factory.

- [ ] **Step 3: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: WindowService dialog factories and close-relay coverage"
```

---

### Task 9: Full verification sweep

**Files:**
- None (verification only)

- [ ] **Step 1: Clean build + both suites**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.IntegrationTests
```

All three must pass. Run the unit suite twice consecutively (polling stability).

- [ ] **Step 2: Boundary greps (spec enforcement)**

```bash
rg -n "IServiceProvider|GetRequiredService" src/TaskManager --glob "*.cs" | rg -v "App.xaml.cs|Composition/"
# expect: no output — service location confined to composition root

rg -n "LegacySettingsMigrator|LegacyUserConfig|MonitoringButtonIcon|\"You need to select options\"" src tests --glob "*.cs" --glob "*.resx"
# expect: no output
```

- [ ] **Step 3: Report**

Summarize: suites green, greps clean, deviations encountered. No commit unless something changed.

---
