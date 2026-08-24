# Services / Application / Business Logic Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the services, application, and business logic layers to achieve genuine separation of concerns, testable ViewModels, and single-responsibility classes.

**Architecture:** Split `ProcessManager` into `ProcessStore` (app layer, owns observable state) and `ProcessOperationsService` (Domain, stateless OS ops). Replace `TimerManager` with `PollingLoop` using `PeriodicTimer`. Introduce `IWindowService` to decouple VMs from window lifecycle. Fix export pipeline, DI bugs, and dead code.

**Tech Stack:** .NET 10, C# 14, WPF, CommunityToolkit.Mvvm 8.4.0, Microsoft.Extensions.DependencyInjection 10.0.11, xUnit v3, NSubstitute 6.2.0

## Global Constraints

- .NET 10.0, `<TargetFramework>net10.0-windows</TargetFramework>`, `<UseWPF>true</UseWPF>`
- CommunityToolkit.Mvvm 8.4.0 for `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`
- Domain project: zero WPF references (BCL + CommunityToolkit.Mvvm + NtApiDotNet + ClosedXML only)
- `INotifyPropertyChanged` on `Process` stays (BCL interface, not WPF)
- All ViewModels must be constructible with NSubstitute fakes — no `IServiceProvider`, no Windows, no global state
- No side effects in ViewModel constructors — startup via explicit `Initialize()`/`InitializeAsync()`
- Existing tests must continue passing after each task

## File Structure

### New Files

| File | Responsibility |
|------|---------------|
| `src/TaskManager.Domain/Services/ProcessOperationsService.cs` | Stateless OS operations (terminate, set-priority) |
| `src/TaskManager.Domain/Abstractions/IDataExporterFactory.cs` | Factory interface for creating exporters |
| `src/TaskManager.Domain/Abstractions/IErrorHandler.cs` | Error handling abstraction (moved from app) |
| `src/TaskManager/Services/ProcessStore.cs` | Observable process list, PID index, enrichment cache, refresh pipeline |
| `src/TaskManager/Services/PollingLoop.cs` | Async polling loop using `PeriodicTimer` |
| `src/TaskManager/Abstractions/IWindowService.cs` | Window lifecycle abstraction |
| `src/TaskManager/Services/WindowService.cs` | Window lifecycle implementation |
| `tests/TaskManager.UnitTests/Services/ProcessOperationsServiceTests.cs` | Unit tests for OS operations |
| `tests/TaskManager.UnitTests/Services/ProcessStoreTests.cs` | Unit tests for process store |
| `tests/TaskManager.UnitTests/Services/PollingLoopTests.cs` | Unit tests for polling loop |

### Modified Files

| File | Changes |
|------|---------|
| `src/TaskManager.Domain/Primitives/LanguageDictionary.cs` | Static `KeysList` property |
| `src/TaskManager.Domain/Services/DataExport/BaseDataExporter.cs` | `WriteFile<T>` template method |
| `src/TaskManager.Domain/Services/DataExport/CsvExporter.cs` | Override `WriteFile<T>` |
| `src/TaskManager.Domain/Services/DataExport/TxtExporter.cs` | Override `WriteFile<T>` |
| `src/TaskManager.Domain/Services/DataExport/JsonExporter.cs` | Override `WriteFile<T>` |
| `src/TaskManager.Domain/Services/DataExport/ExcelExporter.cs` | Override `WriteFile<T>` |
| `src/TaskManager.Domain/Services/DataExport/XmlExporter.cs` | Override `WriteFile<T>` (fixes bug) |
| `src/TaskManager.Domain/Models/Process.cs` | Extend `ObservableObject`, use `[ObservableProperty]` |
| `src/TaskManager.Domain/Models/ProcessItem.cs` | Extend `ObservableObject` |
| `src/TaskManager/ViewModels/MainWindowViewModel.cs` | Remove `IServiceProvider`, add `InitializeAsync()` |
| `src/TaskManager/ViewModels/DataExportWindowViewModel.cs` | Remove `IServiceProvider`, add `Initialize()` |
| `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs` | Remove `IServiceProvider`, add `Initialize()` |
| `src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs` | Remove `GetAssociatedWindow<T>()` |
| `src/TaskManager/App.xaml.cs` | Fix DI, update startup orchestration |
| `src/TaskManager/Services/ErrorHandling/IErrorHandler.cs` | Delete (moved to Domain) |
| `src/TaskManager/Services/ErrorHandling/UiErrorHandler.cs` | Update namespace |

### Deleted Files

| File | Reason |
|------|--------|
| `src/TaskManager.Domain/Services/ProcessManager.cs` | Split into `ProcessStore` + `ProcessOperationsService` |
| `src/TaskManager.Domain/Services/TimerManager.cs` | Replaced by `PollingLoop` |
| `src/TaskManager.Domain/Abstractions/IDispatcherService.cs` | Moved to app layer |
| `src/TaskManager/Services/FolderSelector.cs` | Inlined in `WindowService.SelectFolder()` |
| `src/TaskManager/ViewModels/Preconditions.cs` | Replaced by plain guard methods |
| `src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs` | Replaced by `IWindowService` |
| `src/TaskManager/Services/Factories/DataExportViewModelFactory.cs` | Replaced by `IWindowService` |

---

## Task 1: Bug Fixes (trivial, no structural changes)

**Files:**
- Modify: `src/TaskManager.Domain/Primitives/LanguageDictionary.cs`
- Modify: `src/TaskManager/App.xaml.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/XmlExporter.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: None
- Produces: None (fixes existing behavior)

- [ ] **Step 1: Fix `LanguageDictionary.KeysList` allocation**

```csharp
// src/TaskManager.Domain/Primitives/LanguageDictionary.cs
using System.Globalization;

namespace TaskManager.Domain.Primitives
{
    public class LanguageDictionary : Dictionary<string, CultureInfo>
    {
        public static IReadOnlyList<string> KeysList { get; } =
            new LanguageDictionary().Keys.ToList().AsReadOnly();

        public LanguageDictionary()
        {
            Add("English", new CultureInfo("en"));
            Add("polski", new CultureInfo("pl"));
        }
    }
}
```

- [ ] **Step 2: Fix duplicate `SettingsService` DI registration**

In `src/TaskManager/App.xaml.cs`, remove line 102 (`services.AddSingleton<SettingsService>();`). The interface registration at line 95 is sufficient.

- [ ] **Step 3: Remove duplicate `using` in `App.xaml.cs`**

Remove the second `using TaskManager.Services;` (line 11).

- [ ] **Step 4: Remove commented-out dead code in `App.xaml.cs`**

Remove line 124: `//services.AddTransient<SetPriorityWindow>();`

- [ ] **Step 5: Remove commented-out dead code in `MainWindowViewModel.cs`**

Remove lines 179-185 (the commented-out `GotConfirmation` precondition block).

- [ ] **Step 6: Commit**

```bash
git add src/TaskManager.Domain/Primitives/LanguageDictionary.cs src/TaskManager/App.xaml.cs src/TaskManager/ViewModels/MainWindowViewModel.cs
git commit -m "fix: language dictionary allocation, duplicate DI registration, dead code"
```

---

## Task 2: Extract `ProcessOperationsService` from `ProcessManager`

**Files:**
- Create: `src/TaskManager.Domain/Services/ProcessOperationsService.cs`
- Modify: `src/TaskManager.Domain/Services/ProcessManager.cs` (remove OS operations, add `WritebackPriority`)
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (use `ProcessOperationsService`)
- Modify: `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs` (use `ProcessOperationsService`)
- Modify: `src/TaskManager/App.xaml.cs` (register `ProcessOperationsService`)
- Create: `tests/TaskManager.UnitTests/Services/ProcessOperationsServiceTests.cs`

**Interfaces:**
- Consumes: `ILogger<ProcessOperationsService>` (BCL)
- Produces: `ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int>, Action<int>?)`, `ProcessOpSummary SetPriority(IReadOnlyCollection<int>, ProcessPriorityClass, Action<int>?)`

- [ ] **Step 1: Create `ProcessOperationsService`**

```csharp
// src/TaskManager.Domain/Services/ProcessOperationsService.cs
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    public class ProcessOperationsService
    {
        private readonly ILogger<ProcessOperationsService> _logger;

        public ProcessOperationsService(ILogger<ProcessOperationsService> logger)
        {
            _logger = logger;
        }

        public ProcessOpSummary TerminateProcesses(
            IReadOnlyCollection<int> pids,
            Action<int>? onSuccess = null)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
                _logger.LogDebug("Process {Pid} was terminated", pid);
                onSuccess?.Invoke(pid);
            });
        }

        public ProcessOpSummary SetPriority(
            IReadOnlyCollection<int> pids,
            ProcessPriorityClass priority,
            Action<int>? onSuccess = null)
        {
            return ExecutePerPid(pids, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.PriorityClass = priority;
                _logger.LogDebug("Process {Pid} priority set to {Priority}", pid, priority);
                onSuccess?.Invoke(pid);
            });
        }

        private ProcessOpSummary ExecutePerPid(IEnumerable<int> selectedPids, Action<int> operation)
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

            return new ProcessOpSummary { SucceededPids = succeeded, Failures = failures };
        }

        private const int ErrorAccessDenied = 5;

        private static ProcessOpFailureReason ClassifyFailure(Exception ex) => ex switch
        {
            Win32Exception { NativeErrorCode: ErrorAccessDenied } => ProcessOpFailureReason.AccessDenied,
            ArgumentException => ProcessOpFailureReason.ProcessExited,
            InvalidOperationException => ProcessOpFailureReason.ProcessExited,
            _ => ProcessOpFailureReason.Unknown
        };
    }
}
```

- [ ] **Step 2: Remove OS operations from `ProcessManager`**

Remove from `ProcessManager.cs`: `TerminateProcesses`, `SetPriority`, `ExecutePerPid`, `ClassifyFailure`, `ErrorAccessDenied` constant. Add `WritebackPriority`:

```csharp
// Add to ProcessManager.cs
internal void WritebackPriority(int pid, int newPriority)
{
    lock (_index)
    {
        if (_index.TryGetValue(pid, out var item))
        {
            item.Process.Priority = newPriority;
        }
    }
}
```

- [ ] **Step 3: Update `MainWindowViewModel` to use `ProcessOperationsService`**

Replace `ProcessManager` dependency with `ProcessOperationsService` in constructor. Update `TerminateProcesses()` and `SetPriority()` methods to call the new service and use `ProcessManager.WritebackPriority` for success callbacks.

- [ ] **Step 4: Update `SetPriorityWindowViewModel` to use `ProcessOperationsService`**

Replace `ProcessManager` dependency with `ProcessOperationsService`. Update `OnConfirm()` to call the new service with a `WritebackPriority` callback.

- [ ] **Step 5: Register `ProcessOperationsService` in DI**

Add to `App.xaml.cs` `ConfigureServices`:
```csharp
services.AddSingleton<ProcessOperationsService>();
```

- [ ] **Step 6: Add unit tests**

```csharp
// tests/TaskManager.UnitTests/Services/ProcessOperationsServiceTests.cs
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using Xunit;

namespace TaskManager.UnitTests.Services
{
    public class ProcessOperationsServiceTests
    {
        private readonly ProcessOperationsService _sut;
        private readonly ILogger<ProcessOperationsService> _logger;

        public ProcessOperationsServiceTests()
        {
            _logger = Substitute.For<ILogger<ProcessOperationsService>>();
            _sut = new ProcessOperationsService(_logger);
        }

        [Fact]
        public void TerminateProcesses_InvalidPid_ReturnsFailure()
        {
            var result = _sut.TerminateProcesses([999999]);

            result.HasFailures.ShouldBeTrue();
            result.Failures.First().Reason.ShouldBe(ProcessOpFailureReason.ProcessExited);
        }

        [Fact]
        public void TerminateProcesses_EmptyPids_ReturnsEmptySummary()
        {
            var result = _sut.TerminateProcesses([]);

            result.SucceededPids.ShouldBeEmpty();
            result.Failures.ShouldBeEmpty();
        }

        [Fact]
        public void SetPriority_InvalidPid_ReturnsFailure()
        {
            var result = _sut.SetPriority([999999], System.Diagnostics.ProcessPriorityClass.Normal);

            result.HasFailures.ShouldBeTrue();
        }
    }
}
```

- [ ] **Step 7: Run tests**

```bash
dotnet test tests/TaskManager.UnitTests --filter "ProcessOperationsServiceTests"
```

- [ ] **Step 8: Commit**

```bash
git add src/TaskManager.Domain/Services/ProcessOperationsService.cs src/TaskManager.Domain/Services/ProcessManager.cs src/TaskManager/ViewModels/MainWindowViewModel.cs src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs src/TaskManager/App.xaml.cs tests/TaskManager.UnitTests/Services/ProcessOperationsServiceTests.cs
git commit -m "refactor: extract ProcessOperationsService from ProcessManager"
```

---

## Task 3: Move `IErrorHandler` to Domain abstractions

**Files:**
- Create: `src/TaskManager.Domain/Abstractions/IErrorHandler.cs`
- Delete: `src/TaskManager/Services/ErrorHandling/IErrorHandler.cs`
- Modify: `src/TaskManager/Services/ErrorHandling/UiErrorHandler.cs` (update namespace)
- Modify: `src/TaskManager/Services/ErrorHandling/ErrorHandlerExtensions.cs` (update namespace)
- Modify: All files that import `TaskManager.Services.ErrorHandling` for `IErrorHandler`

**Interfaces:**
- Consumes: None
- Produces: `IErrorHandler` in `TaskManager.Domain.Abstractions`

- [ ] **Step 1: Create `IErrorHandler` in Domain abstractions**

```csharp
// src/TaskManager.Domain/Abstractions/IErrorHandler.cs
namespace TaskManager.Domain.Abstractions
{
    public interface IErrorHandler
    {
        void Handle(Exception exception, string operationContext);
        bool HandleDispatcherException(Exception exception);
        void LogFatal(Exception exception);
        void LogUnobserved(Exception exception);
    }
}
```

- [ ] **Step 2: Delete old `IErrorHandler` from app layer**

Delete `src/TaskManager/Services/ErrorHandling/IErrorHandler.cs`.

- [ ] **Step 3: Update `UiErrorHandler` namespace**

Change `using TaskManager.Services.ErrorHandling;` to `using TaskManager.Domain.Abstractions;` in `UiErrorHandler.cs`. The class stays in `TaskManager.Services.ErrorHandling` namespace (it's an app-layer implementation).

- [ ] **Step 4: Update `ErrorHandlerExtensions` namespace**

Change `using TaskManager.Services.ErrorHandling;` to `using TaskManager.Domain.Abstractions;` in `ErrorHandlerExtensions.cs`. The class stays in `TaskManager.Services.ErrorHandling` namespace.

- [ ] **Step 5: Update all consumer imports**

Files that use `IErrorHandler` need `using TaskManager.Domain.Abstractions;` instead of (or in addition to) `using TaskManager.Services.ErrorHandling;`. Key files:
- `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- `src/TaskManager/ViewModels/DataExportWindowViewModel.cs`
- `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- `src/TaskManager/App.xaml.cs`

- [ ] **Step 6: Run full build**

```bash
dotnet build
```

- [ ] **Step 7: Commit**

```bash
git add src/TaskManager.Domain/Abstractions/IErrorHandler.cs src/TaskManager/Services/ErrorHandling/UiErrorHandler.cs src/TaskManager/Services/ErrorHandling/ErrorHandlerExtensions.cs
git commit -m "refactor: move IErrorHandler to Domain abstractions"
```

---

## Task 4: Models to `ObservableObject`

**Files:**
- Modify: `src/TaskManager.Domain/Models/Process.cs`
- Modify: `src/TaskManager.Domain/Models/ProcessItem.cs`

**Interfaces:**
- Consumes: `ObservableObject` from CommunityToolkit.Mvvm
- Produces: Same public API, reduced boilerplate

- [ ] **Step 1: Refactor `Process` to use `ObservableObject`**

```csharp
// src/TaskManager.Domain/Models/Process.cs
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Models
{
    public partial class Process : ObservableObject, IExportable
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private int? _pid;

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private int? _priority;

        [ObservableProperty]
        private int _threadCount;

        [ObservableProperty]
        private int? _ppid;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ArchitectureTypeDisplay))]
        private ArchitectureType _architectureType;

        [IgnoreSerialization]
        [JsonIgnore]
        public string ArchitectureTypeDisplay => EnumExtensions.ToString(ArchitectureType);

        public override string ToString() => $"{Name} ({Pid})";

        public string ToDelimitedString(char separator)
        {
            return string.Join(separator.ToString(),
                Name,
                Pid,
                Path,
                Priority,
                ThreadCount,
                Ppid);
        }
    }
}
```

- [ ] **Step 2: Refactor `ProcessItem` to use `ObservableObject`**

```csharp
// src/TaskManager.Domain/Models/ProcessItem.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TaskManager.Domain.Models
{
    public partial class ProcessItem : ObservableObject
    {
        public required Process Process { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [SetsRequiredMembers]
        public ProcessItem(Process process)
        {
            Process = process;
        }

        public override string ToString()
        {
            return $"{Process.Name} ({Process.Pid})";
        }
    }
}
```

- [ ] **Step 3: Run full build**

```bash
dotnet build
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/TaskManager.UnitTests
```

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager.Domain/Models/Process.cs src/TaskManager.Domain/Models/ProcessItem.cs
git commit -m "refactor: Process and ProcessItem use ObservableObject"
```

---

## Task 5: Introduce `IWindowService` and kill window factories

**Files:**
- Create: `src/TaskManager/Abstractions/IWindowService.cs`
- Create: `src/TaskManager/Services/WindowService.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/DataExportWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs`
- Modify: `src/TaskManager/App.xaml.cs`
- Delete: `src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs`
- Delete: `src/TaskManager/Services/Factories/DataExportViewModelFactory.cs`
- Create: `src/TaskManager.Domain/Abstractions/IDataExporterFactory.cs`
- Modify: `src/TaskManager/Services/Factories/DataExporterFactory.cs` (implement interface)

**Interfaces:**
- Consumes: `IServiceProvider`, `IMessageService`, all ViewModels
- Produces: `IWindowService` with `ShowSettings()`, `ShowExport()`, `ShowSetPriority()`, `SelectFolder()`, `Confirm()`, `Alert()`

- [ ] **Step 1: Create `IWindowService` interface**

```csharp
// src/TaskManager/Abstractions/IWindowService.cs
using TaskManager.Domain.Models;

namespace TaskManager.Abstractions
{
    public interface IWindowService
    {
        bool ShowSettings();
        bool ShowExport(IReadOnlyList<Process> processes);
        bool ShowSetPriority(IReadOnlyCollection<int> pids);
        string? SelectFolder();
        bool Confirm(string message, string title);
        void Alert(string message, string title);
    }
}
```

- [ ] **Step 2: Create `WindowService` implementation**

```csharp
// src/TaskManager/Services/WindowService.cs
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Services
{
    internal sealed class WindowService : IWindowService
    {
        private readonly IServiceProvider _sp;

        public WindowService(IServiceProvider sp)
        {
            _sp = sp;
        }

        public bool ShowSettings()
        {
            var vm = _sp.GetRequiredService<SettingsWindowViewModel>();
            var window = new SettingsWindow { DataContext = vm };
            return window.ShowDialog() == true;
        }

        public bool ShowExport(IReadOnlyList<Process> processes)
        {
            var vm = _sp.GetRequiredService<DataExportWindowViewModel>();
            vm.Initialize(processes);
            var window = new DataExportWindow { DataContext = vm };
            return window.ShowDialog() == true;
        }

        public bool ShowSetPriority(IReadOnlyCollection<int> pids)
        {
            var vm = _sp.GetRequiredService<SetPriorityWindowViewModel>();
            vm.Initialize(pids);
            var window = new SetPriorityWindow { DataContext = vm };
            return window.ShowDialog() == true;
        }

        public string? SelectFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public bool Confirm(string message, string title)
        {
            var msg = _sp.GetRequiredService<IMessageService>();
            return msg.ShowMessage(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                   == MessageBoxResult.OK;
        }

        public void Alert(string message, string title)
        {
            var msg = _sp.GetRequiredService<IMessageService>();
            msg.ShowMessage(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
```

- [ ] **Step 3: Create `IDataExporterFactory` interface in Domain**

```csharp
// src/TaskManager.Domain/Abstractions/IDataExporterFactory.cs
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;

namespace TaskManager.Domain.Abstractions
{
    public interface IDataExporterFactory
    {
        BaseDataExporter Create(DataType dataType);
    }
}
```

- [ ] **Step 4: Update `DataExporterFactory` to implement interface**

Change class declaration to: `internal class DataExporterFactory : IDataExporterFactory`

- [ ] **Step 5: Strip `ViewModelBase`**

```csharp
// src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskManager.ViewModels.Abstraction
{
    internal class ViewModelBase : ObservableObject
    {
    }
}
```

- [ ] **Step 6: Delete window factories**

Delete `src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs` and `src/TaskManager/Services/Factories/DataExportViewModelFactory.cs`.

- [ ] **Step 7: Register `IWindowService` in DI**

Add to `App.xaml.cs` `ConfigureServices`:
```csharp
services.AddSingleton<IWindowService, WindowService>();
```

Remove old factory registrations:
```csharp
// DELETE these lines:
services.AddTransient<DataExportViewModelFactory>();
services.AddTransient<SetPriorityVVmFactory>();
```

- [ ] **Step 8: Commit**

```bash
git add src/TaskManager/Abstractions/IWindowService.cs src/TaskManager/Services/WindowService.cs src/TaskManager.Domain/Abstractions/IDataExporterFactory.cs src/TaskManager/Services/Factories/DataExporterFactory.cs src/TaskManager/ViewModels/Abstraction/ViewModelBase.cs src/TaskManager/App.xaml.cs
git rm src/TaskManager/Services/Factories/SetPriorityVVmFactory.cs src/TaskManager/Services/Factories/DataExportViewModelFactory.cs
git commit -m "refactor: introduce IWindowService, delete window factories"
```

---

## Task 6: VM constructor hygiene

**Files:**
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/DataExportWindowViewModel.cs`
- Modify: `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- Modify: `src/TaskManager/App.xaml.cs`

**Interfaces:**
- Consumes: `ProcessStore`, `IWindowService`, `IErrorHandler`, `ProcessOperationsService`, `ISettingsService`, `IDataExporterFactory`
- Produces: VMs with clean constructors, `Initialize()`/`InitializeAsync()` methods

- [ ] **Step 1: Refactor `MainWindowViewModel`**

Remove `IServiceProvider`, `IDispatcherService`, `IMessageService` from constructor. Add `InitializeAsync()`:

```csharp
// src/TaskManager/ViewModels/MainWindowViewModel.cs
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
    {
        private readonly ProcessStore _store;
        private readonly IWindowService _windowService;
        private readonly IErrorHandler _errorHandler;
        private readonly ProcessOperationsService _processOps;

        public ReadOnlyObservableCollection<ProcessItem> Processes => _store.Items;

        private int _processCount;
        public int ProcessCount
        {
            get => _processCount;
            private set => SetProperty(ref _processCount, value);
        }

        public ICommand ExportCommand { get; }
        public ICommand TerminateCommand { get; }
        public ICommand SetPriorityCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand RefreshCommand { get; }

        public MainWindowViewModel(
            ProcessStore store,
            IWindowService windowService,
            IErrorHandler errorHandler,
            ProcessOperationsService processOps)
        {
            _store = store;
            _windowService = windowService;
            _errorHandler = errorHandler;
            _processOps = processOps;

            ExportCommand = new RelayCommand(Export);
            TerminateCommand = new RelayCommand(TerminateProcesses);
            SetPriorityCommand = new RelayCommand(SetPriority);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            RefreshCommand = new AsyncRelayCommand(() =>
                _errorHandler.GuardAsync(() => _store.RunRefreshAsync(), "refreshing process list"));

            _store.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessStore.ProcessCount))
                {
                    ProcessCount = _store.ProcessCount;
                }
            };
        }

        public async Task InitializeAsync()
        {
            await _errorHandler.GuardAsync(() => _store.RunRefreshAsync(), "loading initial process list");
        }

        private void OpenSettings()
        {
            _windowService.ShowSettings();
        }

        private void Export()
        {
            var processes = _store.SnapshotForExport();
            _windowService.ShowExport(processes);
        }

        private IEnumerable<ProcessItem> GetSelectedProcesses() => Processes.Where(p => p.IsSelected);

        private void TerminateProcesses()
        {
            if (!Processes.Any(x => x.IsSelected))
            {
                _windowService.Alert(Strings.SelectProcess, Strings.Error);
                return;
            }

            if (!_windowService.Confirm(Strings.AskingForConfirmation, Strings.Confirm))
            {
                return;
            }

            _errorHandler.Guard(() =>
            {
                var summary = _processOps.TerminateProcesses(
                    GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
                ReportPartialFailures(summary);
            });
        }

        private void SetPriority()
        {
            if (!Processes.Any(x => x.IsSelected))
            {
                _windowService.Alert(Strings.SelectProcess, Strings.Error);
                return;
            }

            var pids = GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray();
            _windowService.ShowSetPriority(pids);
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures) return;
            var total = summary.SucceededPids.Count + summary.Failures.Count;
            _windowService.Alert(
                string.Format(Strings.OpsCompletedWithFailuresFormat, summary.SucceededPids.Count, total),
                Strings.Error);
        }
    }
}
```

- [ ] **Step 2: Refactor `DataExportWindowViewModel`**

Remove `IServiceProvider`, `FolderSelector`, `IMessageService`. Add `Initialize()`:

```csharp
// src/TaskManager/ViewModels/DataExportWindowViewModel.cs
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    internal class DataExportWindowViewModel : ViewModelBase
    {
        private readonly ISettingsService _settings;
        private readonly IErrorHandler _errorHandler;
        private readonly IDataExporterFactory _exporterFactory;
        private readonly IWindowService _windowService;
        private IReadOnlyList<Process> _processes = [];

        public ExportationType? Exportation { get; set => SetProperty(ref field, value); }
        public DataType? DataType { get; set => SetProperty(ref field, value); }

        public string DirPath
        {
            get => _dirPath;
            set => SetProperty(ref _dirPath, value);
        }
        private string _dirPath = string.Empty;

        public IList<ExportationType> Exportations { get; } = Enum.GetValues<ExportationType>();
        public IList<DataType> Extensions { get; } = Enum.GetValues<DataType>();

        public ICommand SelectFolderCommand { get; }
        public ICommand OnConfirmClick { get; }

        public DataExportWindowViewModel(
            ISettingsService settings,
            IErrorHandler errorHandler,
            IDataExporterFactory exporterFactory,
            IWindowService windowService)
        {
            _settings = settings;
            _errorHandler = errorHandler;
            _exporterFactory = exporterFactory;
            _windowService = windowService;

            SelectFolderCommand = new RelayCommand(SelectFolder);
            OnConfirmClick = new RelayCommand(OnConfirm);
        }

        public void Initialize(IReadOnlyList<Process> processes)
        {
            _processes = processes.ToArray();
        }

        private void SelectFolder()
        {
            var folder = _windowService.SelectFolder();
            if (folder is not null) DirPath = folder;
        }

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
                {
                    _windowService.Alert("You need to select options", Resources.Languages.Strings.Error);
                    return;
                }

                var exporter = _exporterFactory.Create(dataType);
                var result = exporter.Export(DirPath, _processes);

                if (result.IsSuccess)
                {
                    GetAssociatedWindow<DataExportWindow>().DialogResult = true;
                    return;
                }

                _windowService.Alert(
                    string.Format(Resources.Languages.Strings.ExportFailedFormat, DirPath) + " " +
                    DescribeFailure(result.FailureReason!.Value),
                    Resources.Languages.Strings.Error);
            }, "exporting process data");
        }

        private static string DescribeFailure(ExportFailureReason reason) => reason switch
        {
            ExportFailureReason.AccessDenied => Resources.Languages.Strings.ExportFailedAccessDenied,
            ExportFailureReason.InvalidPath => Resources.Languages.Strings.ExportFailedInvalidPath,
            _ => Resources.Languages.Strings.ExportFailedIo
        };
    }
}
```

- [ ] **Step 3: Refactor `SetPriorityWindowViewModel`**

Remove `IServiceProvider`, `IMessageService`. Add `Initialize()`:

```csharp
// src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Localization;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    internal class SetPriorityWindowViewModel : ViewModelBase
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();
        public ProcessPriorityClass? Priority { get; set => SetProperty(ref field, value); }
        public ICommand OnConfirmCommand { get; }

        private readonly ProcessOperationsService _processOps;
        private readonly ProcessStore _store;
        private readonly IWindowService _windowService;
        private readonly IErrorHandler _errorHandler;
        private IReadOnlyCollection<int> _processIds = [];

        public SetPriorityWindowViewModel(
            ProcessOperationsService processOps,
            ProcessStore store,
            IWindowService windowService,
            IErrorHandler errorHandler)
        {
            _processOps = processOps;
            _store = store;
            _windowService = windowService;
            _errorHandler = errorHandler;
            OnConfirmCommand = new RelayCommand(OnConfirm);
        }

        public void Initialize(IReadOnlyCollection<int> processIds)
        {
            _processIds = processIds;
        }

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Priority is null)
                {
                    _windowService.Alert(Resources.Languages.Strings.Select, Resources.Languages.Strings.Error);
                    return;
                }

                var summary = _processOps.SetPriority(
                    _processIds,
                    Priority.Value,
                    pid => _store.WritebackPriority(pid, Primitives.ProcessBasePriority.Get(Priority.Value)));

                if (summary.HasFailures)
                {
                    var total = summary.SucceededPids.Count + summary.Failures.Count;
                    _windowService.Alert(
                        string.Format(Resources.Languages.Strings.OpsCompletedWithFailuresFormat,
                            summary.SucceededPids.Count, total),
                        Resources.Languages.Strings.Error);
                }

                var window = App.Current.Windows.OfType<SetPriorityWindow>().FirstOrDefault(w => w.DataContext == this);
                window?.Close();
            }, "applying the selected priority");
        }
    }
}
```

- [ ] **Step 4: Update `App.xaml.cs` startup orchestration**

Remove `SetLanguage()` call (language is set in settings service constructor). Update `LaunchGUI`:

```csharp
private void LaunchGUI()
{
    var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
    var store = _serviceProvider.GetRequiredService<ProcessStore>();
    var polling = _serviceProvider.GetRequiredService<PollingLoop>();

    mainWindow.Show();

    // Explicit startup — no constructor side effects
    mainWindow.DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>();
    var vm = (MainWindowViewModel)mainWindow.DataContext;
    _ = vm.InitializeAsync();
    polling.Start();
}
```

- [ ] **Step 5: Run full build**

```bash
dotnet build
```

- [ ] **Step 6: Run tests**

```bash
dotnet test tests/TaskManager.UnitTests
```

- [ ] **Step 7: Commit**

```bash
git add src/TaskManager/ViewModels/MainWindowViewModel.cs src/TaskManager/ViewModels/DataExportWindowViewModel.cs src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs src/TaskManager/App.xaml.cs
git commit -m "refactor: VM constructor hygiene, no side effects, explicit Initialize"
```

---

## Task 7: Replace `TimerManager` with `PollingLoop`

**Files:**
- Create: `src/TaskManager/Services/PollingLoop.cs`
- Delete: `src/TaskManager.Domain/Services/TimerManager.cs`
- Modify: `src/TaskManager/App.xaml.cs`
- Create: `tests/TaskManager.UnitTests/Services/PollingLoopTests.cs`

**Interfaces:**
- Consumes: `ProcessStore`, `ISettingsService`
- Produces: `PollingLoop` with `Start()`, `Stop()`, `Restart()`

- [ ] **Step 1: Create `PollingLoop`**

```csharp
// src/TaskManager/Services/PollingLoop.cs
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

namespace TaskManager.Services
{
    public class PollingLoop : IDisposable
    {
        private readonly ProcessStore _store;
        private readonly ISettingsService _settings;
        private CancellationTokenSource _cts = new();
        private Task? _running;

        public PollingLoop(ProcessStore store, ISettingsService settings)
        {
            _store = store;
            _settings = settings;
            _settings.Changed += OnSettingsChanged;
        }

        public void Start()
        {
            if (_cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();

            _running = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
            _running?.Wait(TimeSpan.FromSeconds(5));
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var seconds = RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency];
                if (seconds == 0) break; // Paused

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

                try
                {
                    while (await timer.WaitForNextTickAsync(ct))
                    {
                        await _store.RunRefreshAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            Restart();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
```

- [ ] **Step 2: Delete `TimerManager`**

Delete `src/TaskManager.Domain/Services/TimerManager.cs`.

- [ ] **Step 3: Update `App.xaml.cs`**

Remove `TimerManager` registration. Add `PollingLoop` registration:
```csharp
services.AddSingleton<PollingLoop>();
```

- [ ] **Step 4: Add unit tests**

```csharp
// tests/TaskManager.UnitTests/Services/PollingLoopTests.cs
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services;
using Xunit;

namespace TaskManager.UnitTests.Services
{
    public class PollingLoopTests
    {
        [Fact]
        public void PollingLoop_CanBeCreated()
        {
            var store = Substitute.For<ProcessStore>(
                Substitute.For<ISystemProcessEnumerator>(),
                Substitute.For<ProcessEnricher>(Substitute.For<Microsoft.Extensions.Logging.ILogger<ProcessEnricher>>()),
                Substitute.For<IDispatcherService>(),
                Substitute.For<Microsoft.Extensions.Logging.ILogger<ProcessStore>>());
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Defaults);

            var loop = new PollingLoop(store, settings);

            loop.ShouldNotBeNull();
        }
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test tests/TaskManager.UnitTests
```

- [ ] **Step 6: Commit**

```bash
git add src/TaskManager/Services/PollingLoop.cs src/TaskManager/App.xaml.cs tests/TaskManager.UnitTests/Services/PollingLoopTests.cs
git rm src/TaskManager.Domain/Services/TimerManager.cs
git commit -m "refactor: replace TimerManager with PollingLoop using PeriodicTimer"
```

---

## Task 8: Fix export pipeline

**Files:**
- Modify: `src/TaskManager.Domain/Services/DataExport/BaseDataExporter.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/CsvExporter.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/TxtExporter.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/JsonExporter.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/ExcelExporter.cs`
- Modify: `src/TaskManager.Domain/Services/DataExport/XmlExporter.cs`

**Interfaces:**
- Consumes: `ISettingsService`, `ILogger<BaseDataExporter>`
- Produces: `ExportResult Export<T>(string dirPath, IReadOnlyList<T> records)`

- [ ] **Step 1: Refactor `BaseDataExporter`**

```csharp
// src/TaskManager.Domain/Services/DataExport/BaseDataExporter.cs
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services.DataExport
{
    public abstract class BaseDataExporter
    {
        protected const string FileNamePrefix = @"\record-";
        protected string DateTime => _settings.Current.DateTimeFormat;
        protected abstract string Extension { get; }

        private readonly ISettingsService _settings;
        private readonly ILogger<BaseDataExporter> _logger;

        public BaseDataExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public ExportResult Export<T>(string dirPath, IReadOnlyList<T> records) where T : IExportable
        {
            try
            {
                string fullFileName = dirPath + GenerateFileName(Extension);
                PerformExport(fullFileName, records);
                return ExportResult.Success(fullFileName);
            }
            catch (Exception ex) when (TryClassifyFailure(ex, out var reason))
            {
                _logger.LogWarning(ex, "Export as {Extension} failed ({Reason})", Extension, reason);
                return ExportResult.Fail(reason);
            }
        }

        protected abstract void PerformExport<T>(string fullFileName, IReadOnlyList<T> records) where T : IExportable;

        protected string GenerateFileName(string extension)
        {
            return $"{FileNamePrefix}{System.DateTime.Now.ToString(DateTime)}.{extension}";
        }

        private static bool TryClassifyFailure(Exception ex, out ExportFailureReason reason)
        {
            switch (ex)
            {
                case ArgumentException or NotSupportedException:
                    reason = ExportFailureReason.InvalidPath;
                    return true;
                case UnauthorizedAccessException:
                    reason = ExportFailureReason.AccessDenied;
                    return true;
                case IOException:
                    reason = ExportFailureReason.IoError;
                    return true;
                default:
                    reason = default;
                    return false;
            }
        }
    }
}
```

- [ ] **Step 2: Update `CsvExporter`**

```csharp
// src/TaskManager.Domain/Services/DataExport/CsvExporter.cs
using System.IO;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
    public class CsvExporter : BaseDataExporter
    {
        private const char Separator = ',';
        protected override string Extension => "csv";

        public CsvExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger) { }

        protected override void PerformExport<T>(string fullFileName, IReadOnlyList<T> records)
        {
            File.WriteAllLines(fullFileName, records.Select(r => r.ToDelimitedString(Separator)));
        }
    }
}
```

- [ ] **Step 3: Update `TxtExporter`**

```csharp
// src/TaskManager.Domain/Services/DataExport/TxtExporter.cs
using System.IO;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
    public class TxtExporter : BaseDataExporter
    {
        private const char Separator = ' ';
        protected override string Extension => "txt";

        public TxtExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger) { }

        protected override void PerformExport<T>(string fullFileName, IReadOnlyList<T> records)
        {
            File.WriteAllLines(fullFileName, records.Select(r => r.ToDelimitedString(Separator)));
        }
    }
}
```

- [ ] **Step 4: Update `JsonExporter`**

```csharp
// src/TaskManager.Domain/Services/DataExport/JsonExporter.cs
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Services.DataExport
{
    public class JsonExporter : BaseDataExporter
    {
        protected override string Extension => "json";

        public JsonExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger) { }

        protected override void PerformExport<T>(string fullFileName, IReadOnlyList<T> records)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(fullFileName, JsonSerializer.Serialize(records, options));
        }
    }
}
```

- [ ] **Step 5: Update `ExcelExporter`**

```csharp
// src/TaskManager.Domain/Services/DataExport/ExcelExporter.cs
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json.Serialization;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Services.DataExport
{
    public class ExcelExporter : BaseDataExporter
    {
        protected override string Extension => "xlsx";

        public ExcelExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger) { }

        protected override void PerformExport<T>(string fullFileName, IReadOnlyList<T> records)
        {
            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Records");

            string[] headers = GetColumnHeaders(typeof(T));
            for (int i = 0; i < headers.Length; i++)
                worksheet.Cell(1, i + 1).Value = headers[i];

            for (int i = 0; i < records.Count; i++)
            {
                var props = typeof(T).GetProperties()
                    .Where(p => !p.IsDefined(typeof(IgnoreSerialization), false) &&
                                !p.IsDefined(typeof(JsonIgnoreAttribute), false))
                    .ToArray();

                for (int j = 0; j < props.Length; j++)
                    worksheet.Cell(i + 2, j + 1).Value = props[j].GetValue(records[i])?.ToString() ?? "";
            }

            var range = worksheet.Range(1, 1, records.Count + 1, headers.Length);
            var table = range.CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium9;
            table.ShowAutoFilter = true;
            table.Name = "Records";

            workbook.SaveAs(fullFileName);
        }

        private string[] GetColumnHeaders(Type t)
        {
            return t.GetProperties()
                .Where(prop => !prop.IsDefined(typeof(IgnoreSerialization), false) &&
                               !prop.IsDefined(typeof(JsonIgnoreAttribute), false))
                .Select(prop => prop.Name)
                .ToArray();
        }
    }
}
```

- [ ] **Step 6: Update `XmlExporter` (fixes the bug)**

```csharp
// src/TaskManager.Domain/Services/DataExport/XmlExporter.cs
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Services.DataExport
{
    public class XmlExporter : BaseDataExporter
    {
        protected override string Extension => "xml";

        public XmlExporter(ISettingsService settings, ILogger<BaseDataExporter> logger)
            : base(settings, logger) { }

        protected override void PerformExport<T>(string fullFileName, IReadOnlyList<T> records)
        {
            var root = new XElement("Records",
                records.Select(record =>
                    new XElement("Record",
                        typeof(T).GetProperties()
                            .Where(prop => !prop.IsDefined(typeof(IgnoreSerialization), false))
                            .Select(prop => new XElement(prop.Name, prop.GetValue(record)?.ToString() ?? string.Empty))
                    )
                )
            );

            var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
            xmlDoc.Save(fullFileName);
        }
    }
}
```

- [ ] **Step 7: Run tests**

```bash
dotnet test tests/TaskManager.UnitTests --filter "BaseDataExporterTests"
```

- [ ] **Step 8: Commit**

```bash
git add src/TaskManager.Domain/Services/DataExport/
git commit -m "refactor: export pipeline uses WriteFile<T> template, fixes XmlExporter bug"
```

---

## Task 9: Delete remaining dead code

**Files:**
- Delete: `src/TaskManager/Services/FolderSelector.cs`
- Delete: `src/TaskManager/ViewModels/Preconditions.cs`
- Delete: `src/TaskManager.Domain/Abstractions/IDispatcherService.cs`
- Move: `src/TaskManager/Abstractions/IMessageService.cs` stays (used by `WindowService`)

**Interfaces:**
- Consumes: None
- Produces: None (cleanup only)

- [ ] **Step 1: Delete `FolderSelector`**

Delete `src/TaskManager/Services/FolderSelector.cs`. Folder picking is now in `WindowService.SelectFolder()`.

- [ ] **Step 2: Delete `Preconditions` enum**

Delete `src/TaskManager/ViewModels/Preconditions.cs`. Guard logic is now inline in VM command handlers.

- [ ] **Step 3: Delete `IDispatcherService` from Domain**

Delete `src/TaskManager.Domain/Abstractions/IDispatcherService.cs`. The interface moves to the app layer (already exists as `WpfDispatcherService` implements it). Create `src/TaskManager/Abstractions/IDispatcherService.cs` if needed:

```csharp
// src/TaskManager/Abstractions/IDispatcherService.cs
namespace TaskManager.Abstractions
{
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
```

- [ ] **Step 4: Update `WpfDispatcherService` namespace**

Change from `TaskManager.Domain.Abstractions` to `TaskManager.Abstractions`.

- [ ] **Step 5: Update `ProcessStore` import**

Change `using TaskManager.Domain.Abstractions;` to `using TaskManager.Abstractions;` for `IDispatcherService`.

- [ ] **Step 6: Run full build**

```bash
dotnet build
```

- [ ] **Step 7: Run all tests**

```bash
dotnet test tests/TaskManager.UnitTests
```

- [ ] **Step 8: Commit**

```bash
git rm src/TaskManager/Services/FolderSelector.cs src/TaskManager/ViewModels/Preconditions.cs src/TaskManager.Domain/Abstractions/IDispatcherService.cs
git add src/TaskManager/Abstractions/IDispatcherService.cs src/TaskManager/Services/WpfDispatcherService.cs src/TaskManager/Services/ProcessStore.cs
git commit -m "chore: delete dead code, move IDispatcherService to app layer"
```

---

## Task 10: Rename `ProcessManager` to `ProcessStore` and final cleanup

**Files:**
- Rename: `src/TaskManager.Domain/Services/ProcessManager.cs` → `src/TaskManager/Services/ProcessStore.cs`
- Modify: All files that reference `ProcessManager`
- Modify: All test files that reference `ProcessManager`

**Interfaces:**
- Consumes: `ISystemProcessEnumerator`, `ProcessEnricher`, `IDispatcherService`, `ISettingsService`, `ILogger<ProcessStore>`
- Produces: `ProcessStore` with `Items`, `ProcessCount`, `SnapshotForExport()`, `RunRefreshAsync()`, `WritebackPriority()`

- [ ] **Step 1: Create `ProcessStore` in app layer**

Move the state-related code from `ProcessManager` to `src/TaskManager/Services/ProcessStore.cs`. The class keeps: `_items`, `_index`, `_enrichment`, `_refreshGate`, `Items`, `ProcessCount`, `SnapshotForExport()`, `RunRefreshAsync()`, `WritebackPriority()`. Remove all OS operations (already in `ProcessOperationsService`).

- [ ] **Step 2: Update all references**

Replace `ProcessManager` with `ProcessStore` in:
- `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- `src/TaskManager/App.xaml.cs`
- All test files

- [ ] **Step 3: Delete old `ProcessManager.cs`**

Delete `src/TaskManager.Domain/Services/ProcessManager.cs`.

- [ ] **Step 4: Update DI registration**

Replace `services.AddSingleton<ProcessManager>();` with `services.AddSingleton<ProcessStore>();` in `App.xaml.cs`.

- [ ] **Step 5: Run full build**

```bash
dotnet build
```

- [ ] **Step 6: Run all tests**

```bash
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.IntegrationTests
```

- [ ] **Step 7: Final cleanup check**

```bash
# Verify no references to deleted types
grep -r "ProcessManager" src/ --include="*.cs" | grep -v "//"
grep -r "TimerManager" src/ --include="*.cs"
grep -r "FolderSelector" src/ --include="*.cs"
grep -r "Preconditions" src/ --include="*.cs"
grep -r "SetPriorityVVmFactory" src/ --include="*.cs"
grep -r "DataExportViewModelFactory" src/ --include="*.cs"
```

- [ ] **Step 8: Commit**

```bash
git add src/TaskManager/Services/ProcessStore.cs
git rm src/TaskManager.Domain/Services/ProcessManager.cs
git commit -m "refactor: rename ProcessManager to ProcessStore, final cleanup"
```
