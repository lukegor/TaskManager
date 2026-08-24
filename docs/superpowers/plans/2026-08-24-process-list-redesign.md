# Process List Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the process-list pipeline around a single read-only store with snapshot-first O(n) diffing, in-place row updates, and materialized snapshots at every escaping boundary.

**Architecture:** `ProcessManager` owns one `ObservableCollection` exposed via `ReadOnlyObservableCollection`, indexed by PID (`Dictionary<int, ProcessItem>`). Each tick captures a cheap OS snapshot through an `ISystemProcessEnumerator` seam, diffs it off-thread against the index, enriches only new PIDs, then applies adds/updates/removes in one dispatcher batch. The ViewModel stops duplicating collection state; export receives immutable snapshots.

**Tech Stack:** .NET 10 (net10.0-windows), WPF, C# 14 field-backed properties, NtApiDotNet 1.1.33, xunit.v3 + Shouldly + NSubstitute, central package management.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-24-process-list-redesign-design.md`.
- Validation for every task: `dotnet build TaskManager.slnx` then filtered or full `dotnet test TaskManager.Tests/TaskManager.Tests.csproj`.
- Target framework is `net10.0-windows` (set in `Directory.Build.props`) — never add per-project TFM.
- Central package management: never put `Version` on a `PackageReference`; versions live only in `Directory.Packages.props`.
- Match existing test conventions: xunit.v3 `[Fact]`/`[Theory]`, Shouldly assertions (`ShouldBe`, `ShouldBeEmpty`...), NSubstitute for interface stubs.
- Windows-only APIs (P/Invoke, NtApiDotNet) are fine — app is Windows-only.
- Keep XML doc comments in the style of existing Domain files; no inline `//` commentary beyond what exists in shown code.
- `Process.Pid` stays `int?` (public model shape unchanged); pipeline treats it as always-set because every stored item comes from a snapshot with non-null Pid.
- Do not touch `TimerManager`, exporters, `BetterDataGrid`, settings plumbing — out of scope.

---

### Task 1: System-process snapshot seam

**Files:**
- Create: `TaskManager.Domain/Abstractions/ISystemProcessEnumerator.cs`
- Create: `TaskManager.Domain/Models/ProcessSnapshot.cs`
- Create: `TaskManager.Domain/Services/NtSystemProcessEnumerator.cs`
- Test: `TaskManager.Tests/Integration Tests/ProcessManagementTests/NtSystemProcessEnumeratorTests.cs`

**Interfaces:**
- Consumes: nothing new (NtApiDotNet already referenced by `TaskManager.Domain.csproj`).
- Produces:
  ```csharp
  namespace TaskManager.Domain.Abstractions
  public interface ISystemProcessEnumerator
  {
      IReadOnlyList<ProcessSnapshot> Capture();
  }

  namespace TaskManager.Domain.Models
  public sealed record ProcessSnapshot(int Pid, string Name, int ThreadCount, int? Ppid, int? BasePriority);

  namespace TaskManager.Domain.Services
  public sealed class NtSystemProcessEnumerator : ISystemProcessEnumerator
  ```

- [ ] **Step 1: Create the abstraction**

`TaskManager.Domain/Abstractions/ISystemProcessEnumerator.cs`:

```csharp
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    /// <summary>
    /// Cheap whole-system process snapshot (no per-process handle opens).
    /// </summary>
    public interface ISystemProcessEnumerator
    {
        /// <exception cref="Exception">OS-level enumeration failure; callers treat as whole-tick failure.</exception>
        IReadOnlyList<ProcessSnapshot> Capture();
    }
}
```

- [ ] **Step 2: Create the snapshot record**

`TaskManager.Domain/Models/ProcessSnapshot.cs`:

```csharp
namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Per-process data obtainable from one system call, before any handle is opened.
    /// </summary>
    public sealed record ProcessSnapshot(
        int Pid,
        string Name,
        int ThreadCount,
        int? Ppid,
        int? BasePriority);
}
```

- [ ] **Step 3: Implement the NtApiDotNet enumerator**

Verified API surface of NtApiDotNet 1.1.33: `NtSystemInfo.GetProcessInformation()` returns `IEnumerable<NtProcessInformation>` with `int ProcessId`, `int ParentProcessId`, `string ImageName`, `int BasePriority`, `IEnumerable Threads`.

`TaskManager.Domain/Services/NtSystemProcessEnumerator.cs`:

```csharp
using NtApiDotNet;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    /// <remarks>
    /// One system call returns name/PID/PPID/priority/thread-count for ALL processes,
    /// including protected ones that <see cref="System.Diagnostics.Process.GetProcesses"/>
    /// cannot open handles to.
    /// </remarks>
    public sealed class NtSystemProcessEnumerator : ISystemProcessEnumerator
    {
        public IReadOnlyList<ProcessSnapshot> Capture()
        {
            return NtSystemInfo.GetProcessInformation()
                .Select(p => new ProcessSnapshot(
                    p.ProcessId,
                    p.ImageName ?? string.Empty,
                    p.Threads?.Count() ?? 0,
                    p.ParentProcessId,
                    p.BasePriority))
                .ToList();
        }
    }
}
```

- [ ] **Step 4: Add the integration test**

Create `TaskManager.Tests/Integration Tests/ProcessManagementTests/NtSystemProcessEnumeratorTests.cs` with exactly this content:

```csharp
using System.Diagnostics;
using TaskManager.Domain.Services;

namespace TaskManager.Tests
{
    public class NtSystemProcessEnumeratorTests
    {
        [Fact]
        public void Capture_IncludesSelfPid_WithName()
        {
            var snapshots = new NtSystemProcessEnumerator().Capture();

            var self = snapshots.SingleOrDefault(s => s.Pid == Environment.ProcessId);
            self.ShouldNotBeNull();
            self.Name.ShouldNotBeEmpty();
            self.ThreadCount.ShouldBeGreaterThan(0);
            self.Ppid.ShouldNotBeNull();
            self.BasePriority.ShouldNotBeNull();
        }

        [Fact]
        public void Capture_PidsAreUnique()
        {
            var snapshots = new NtSystemProcessEnumerator().Capture();

            snapshots.Select(s => s.Pid).ShouldBeUnique();
        }

        [Fact]
        public void Capture_AtLeastMatchesHandleEnumerationCount()
        {
            // protected/system processes appear here but cannot be opened by handle-based enumeration
            var snapshotPids = new NtSystemProcessEnumerator().Capture().Select(s => s.Pid).ToHashSet();
            int handleVisible = System.Diagnostics.Process.GetProcesses().Count(p =>
            {
                try { return !string.IsNullOrEmpty(p.ProcessName); } catch { return false; }
            });

            snapshotPids.Count.ShouldBeGreaterThanOrEqualTo(handleVisible);
        }
    }
}
```

- [ ] **Step 5: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~NtSystemProcessEnumeratorTests"
```

- [ ] **Step 6: Commit**

```bash
git add "TaskManager.Domain/Abstractions/ISystemProcessEnumerator.cs" "TaskManager.Domain/Models/ProcessSnapshot.cs" "TaskManager.Domain/Services/NtSystemProcessEnumerator.cs" "TaskManager.Tests/Integration Tests/ProcessManagementTests/NtSystemProcessEnumeratorTests.cs"
git commit -m "feat(domain): cheap system-process snapshot seam via NtApiDotNet"
```

---

### Task 2: Observable `Process` model

**Files:**
- Modify: `TaskManager.Domain/Models/Process.cs`
- Test: `TaskManager.Tests/Models/ProcessItemTests.cs` (append a new test class in same file OR create `TaskManager.Tests/Models/ProcessObservableTests.cs` — use the new file)

**Interfaces:**
- Consumes: current `Process` shape (`Name`, `Pid`, `ArchitectureType`, `Path`, `Priority`, `ThreadCount`, `Ppid`, `ArchitectureTypeDisplay`, `ToDelimitedString`).
- Produces: `Process : IExportable, INotifyPropertyChanged`. Every settable property raises `PropertyChanged` with its own name when the value actually changes; no event when equal. `ArchitectureType` setter additionally raises `nameof(ArchitectureTypeDisplay)`. Property names exactly: `"Name"`, `"Pid"`, `"ArchitectureType"`, `"Path"`, `"Priority"`, `"ThreadCount"`, `"Ppid"`, `"ArchitectureTypeDisplay"`.

- [ ] **Step 1: Rewrite `Process.cs`**

Full replacement content:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Represents Windows System Process data (core business logic).
    /// Raises per-field change notifications so bound grid rows update in place.
    /// </summary>
    public class Process : IExportable, INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;
        private int? _pid;
        private ArchitectureType _architectureType;
        private int? _priority;
        private int _threadCount;
        private int? _ppid;

        public event PropertyChangedEventHandler? PropertyChanged;

        public required string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public int? Pid
        {
            get => _pid;
            set => SetField(ref _pid, value);
        }

        [IgnoreSerialization]
        [JsonIgnore]
        public ArchitectureType ArchitectureType
        {
            get => _architectureType;
            set
            {
                if (_architectureType == value)
                {
                    return;
                }

                _architectureType = value;
                Notify(nameof(ArchitectureType));
                Notify(nameof(ArchitectureTypeDisplay));
            }
        }

        public required string Path
        {
            get => _path;
            set => SetField(ref _path, value);
        }

        public int? Priority
        {
            get => _priority;
            set => SetField(ref _priority, value);
        }

        public int ThreadCount
        {
            get => _threadCount;
            set => SetField(ref _threadCount, value);
        }

        public int? Ppid
        {
            get => _ppid;
            set => SetField(ref _ppid, value);
        }

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

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            Notify(propertyName!);
        }

        private void Notify(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

Note: `required` members now have explicit backing fields initialized to safe defaults, satisfying nullable analysis without changing construction sites (`new Process { Name = ..., Path = ... }` still compiles).

- [ ] **Step 2: Add tests**

Create `TaskManager.Tests/Models/ProcessObservableTests.cs`:

```csharp
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Tests
{
    public class ProcessObservableTests
    {
        [Fact]
        public void ChangedValue_RaisesPropertyChangedForThatProperty()
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.Name = "b";

            changed.ShouldHaveSingleItem().ShouldBe(nameof(Process.Name));
            process.Name.ShouldBe("b");
        }

        [Theory]
        [InlineData(10, 10, false)]
        [InlineData(10, 6, true)]
        public void Priority_RaisesOnlyOnActualChange(int initial, int updated, bool expectEvent)
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty, Priority = initial };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.Priority = updated;

            changed.Count.ShouldBe(expectEvent ? 1 : 0);
        }

        [Fact]
        public void ArchitectureChange_RaisesBothDataAndDisplayNotifications()
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.ArchitectureType = ArchitectureType._64BIT;

            changed.ShouldBe(new[] { nameof(Process.ArchitectureType), nameof(Process.ArchitectureTypeDisplay) });
        }

        [Fact]
        public void DelimitedString_UnchangedByRefactor()
        {
            var process = new Process { Name = "app", Pid = 42, Path = @"C:\app.exe", Priority = 8, ThreadCount = 3, Ppid = 4 };

            process.ToDelimitedString(',').ShouldBe("app,42,C:\\app.exe,8,3,4");
        }
    }
}
```

- [ ] **Step 3: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessObservableTests"
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessItemTests"
dotnet test TaskManager.Tests --filter "FullyQualifiedName~BaseDataExporterTests"
```

(`ProcessItemTests` and exporter tests guard the unchanged serialized/delimited behavior.)

- [ ] **Step 4: Commit**

```bash
git add "TaskManager.Domain/Models/Process.cs" "TaskManager.Tests/Models/ProcessObservableTests.cs"
git commit -m "feat(domain): Process model raises per-field change notifications"
```

---

### Task 3: Pure diff engine

**Files:**
- Create: `TaskManager.Domain/Services/ProcessDiffEngine.cs`
- Test: `TaskManager.Tests/ProcessPipeline/ProcessDiffEngineTests.cs`

**Interfaces:**
- Consumes: `ProcessSnapshot` (Task 1), `Process` (existing).
- Produces:
  ```csharp
  namespace TaskManager.Domain.Models
  public sealed record ProcessListDiff(
      IReadOnlyList<ProcessSnapshot> Added,
      IReadOnlyList<int> Removed,
      IReadOnlyList<ProcessSnapshot> Updated);

  namespace TaskManager.Domain.Services
  public static class ProcessDiffEngine
  {
      public static ProcessListDiff Compute(
          IReadOnlyDictionary<int, Process> current,
          IReadOnlyList<ProcessSnapshot> snapshot);
  }
  ```
  Semantics: `Added` = snapshot PIDs missing from `current` (snapshot order); `Removed` = current keys missing from snapshot (ascending PID order); `Updated` = PIDs present in both where any of `Name`, `ThreadCount`, `Priority`, `Ppid` differs between stored values and snapshot values (PID itself never differs — it is the key).

- [ ] **Step 1: Create diff result record + engine**

`TaskManager.Domain/Services/ProcessDiffEngine.cs`:

```csharp
using TaskManager.Domain.Models;

namespace TaskManager.Domain.Services
{
    public sealed record ProcessListDiff(
        IReadOnlyList<ProcessSnapshot> Added,
        IReadOnlyList<int> Removed,
        IReadOnlyList<ProcessSnapshot> Updated)
    {
        public static readonly ProcessListDiff Empty = new([], [], []);
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Updated.Count == 0;
    }

    /// <summary>
    /// Pure function: reconcile last-applied store state against a fresh snapshot.
    /// </summary>
    public static class ProcessDiffEngine
    {
        public static ProcessListDiff Compute(
            IReadOnlyDictionary<int, Process> current,
            IReadOnlyList<ProcessSnapshot> snapshot)
        {
            List<ProcessSnapshot> added = [];
            List<int> removed = [];
            List<ProcessSnapshot> updated = [];

            HashSet<int> snapshotted = new(snapshot.Select(s => s.Pid));

            foreach (var s in snapshot)
            {
                if (!current.TryGetValue(s.Pid, out var stored))
                {
                    added.Add(s);
                    continue;
                }

                if (stored.Name != s.Name ||
                    stored.ThreadCount != s.ThreadCount ||
                    stored.Priority != s.BasePriority ||
                    stored.Ppid != s.Ppid)
                {
                    updated.Add(s);
                }
            }

            removed.AddRange(current.Keys.Where(pid => !snapshotted.Contains(pid)).OrderBy(pid => pid));

            return new ProcessListDiff(added, removed, updated);
        }
    }
}
```

- [ ] **Step 2: Add unit tests**

Create `TaskManager.Tests/ProcessPipeline/ProcessDiffEngineTests.cs`:

```csharp
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;

namespace TaskManager.Tests
{
    public class ProcessDiffEngineTests
    {
        private static Process Stored(int pid, string name = "n", int threads = 1, int? priority = 8, int? ppid = 4) =>
            new() { Name = name, Pid = pid, ThreadCount = threads, Priority = priority, Ppid = ppid, Path = string.Empty };

        private static ProcessSnapshot Snap(int pid, string name = "n", int threads = 1, int? priority = 8, int? ppid = 4) =>
            new(pid, name, threads, ppid, priority);

        [Fact]
        public void EmptyStore_AllSnapshotsAreAdded()
        {
            var diff = ProcessDiffEngine.Compute(
                new Dictionary<int, Process>(),
                [Snap(1), Snap(2)]);

            diff.Added.Select(s => s.Pid).ShouldBe(new[] { 1, 2 });
            diff.Removed.ShouldBeEmpty();
            diff.Updated.ShouldBeEmpty();
            diff.IsEmpty.ShouldBeFalse();
        }

        [Fact]
        public void VanishedPids_AreRemoved_InAscendingOrder()
        {
            var current = new Dictionary<int, Process> { [9] = Stored(9), [5] = Stored(5), [7] = Stored(7) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(7)]);

            diff.Removed.ShouldBe(new[] { 5, 9 });
            diff.Added.ShouldBeEmpty();
            diff.Updated.ShouldBeEmpty();
        }

        [Fact]
        public void IdenticalState_IsEmpty()
        {
            var current = new Dictionary<int, Process> { [1] = Stored(1), [2] = Stored(2) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(1), Snap(2)]);

            diff.IsEmpty.ShouldBeTrue();
        }

        [Theory]
        [InlineData("other", 1, 8, 4)]   // name changed
        [InlineData("n", 3, 8, 4)]       // thread count changed
        [InlineData("n", 1, 6, 4)]       // priority changed
        [InlineData("n", 1, 8, 100)]     // ppid changed
        public void AnyFieldDelta_QualifiesAsUpdated(string name, int threads, int? priority, int? ppid)
        {
            var current = new Dictionary<int, Process> { [1] = Stored(1) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(1, name, threads, priority, ppid)]);

            diff.Updated.Select(s => s.Pid).ShouldBe(new[] { 1 });
        }

        [Fact]
        public void MixedCycle_ClassifiesEachBucket()
        {
            var current = new Dictionary<int, Process>
            {
                [1] = Stored(1),
                [2] = Stored(2, name: "old"),
                [3] = Stored(3),
            };

            var diff = ProcessDiffEngine.Compute(current,
                [Snap(2, name: "new"), Snap(3), Snap(4)]);

            diff.Added.Select(s => s.Pid).ShouldBe(new[] { 4 });
            diff.Removed.ShouldBe(new[] { 1 });
            diff.Updated.Select(s => s.Pid).ShouldBe(new[] { 2 });
        }
    }
}
```

- [ ] **Step 3: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessDiffEngineTests"
```

- [ ] **Step 4: Commit**

```bash
git add "TaskManager.Domain/Services/ProcessDiffEngine.cs" "TaskManager.Tests/ProcessPipeline/ProcessDiffEngineTests.cs"
git commit -m "feat(domain): pure O(n) process-list diff engine"
```

---

### Task 4: Stateless per-PID enricher

**Files:**
- Create: `TaskManager.Domain/Services/ProcessEnricher.cs`
- Test: `TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessEnricherTests.cs`

**Interfaces:**
- Consumes: `ArchitectureType` (TaskManager.Utility).
- Produces:
  ```csharp
  namespace TaskManager.Domain.Services
  public readonly record struct ProcessEnrichment(string Path, ArchitectureType Architecture);
  public sealed class ProcessEnricher
  {
      public bool TryEnrich(int pid, out ProcessEnrichment enrichment);
  }
  ```
  Semantics: opens one handle (`System.Diagnostics.Process.GetProcessById`), reads image path via `MainModule.FileName` (empty string when unavailable) and WOW64 bitness via `IsWow64Process`. Returns `false` on any expected failure (process exited, access denied); logs at Debug. This moves the P/Invoke currently living in `ProcessManager` (it will be deleted from there in Task 5).

- [ ] **Step 1: Implement enricher**

`TaskManager.Domain/Services/ProcessEnricher.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services
{
    public readonly record struct ProcessEnrichment(string Path, ArchitectureType Architecture);

    /// <summary>
    /// Expensive per-PID work requiring a handle: image path and WOW64 bitness.
    /// Stateless — caching (once per PID, both values immutable while running) is the caller's job.
    /// </summary>
    public sealed class ProcessEnricher(ILogger<ProcessEnricher> logger)
    {
        public bool TryEnrich(int pid, out ProcessEnrichment enrichment)
        {
            enrichment = default;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                bool? is32Bit = null;
                if (TryGetProcessBitness(process.Handle, out bool wow64))
                {
                    is32Bit = wow64;
                }

                enrichment = new ProcessEnrichment(
                    process.MainModule?.FileName ?? string.Empty,
                    is32Bit switch
                    {
                        true => ArchitectureType._32BIT,
                        false => ArchitectureType._64BIT,
                        null => ArchitectureType.Unknown
                    });
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogDebug(ex, "Enrichment failed for PID {Pid}; keeping snapshot-level data", pid);
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(nint hProcess, out bool wow64Process);

        private bool TryGetProcessBitness(nint processHandle, out bool is32Bit)
        {
            if (IsWow64Process(processHandle, out is32Bit)) { return true; }

            int errorCode = Marshal.GetLastWin32Error();
            logger.LogDebug("IsWow64Process failed for handle {Handle}. Error code: {ErrorCode}", processHandle, errorCode);
            return false;
        }
    }
}
```

(`TryGetProcessBitness` is an instance method because it logs through the primary-constructor parameter.)

- [ ] **Step 2: Add integration tests**

`TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessEnricherTests.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using WinProcess = System.Diagnostics.Process;

namespace TaskManager.Tests
{
    public class ProcessEnricherTests
    {
        private static readonly ProcessEnricher Enricher = new(NullLogger<ProcessEnricher>.Instance);

        [Fact]
        public void SelfProcess_EnrichesWithPathAndConcreteBitness()
        {
            bool ok = Enricher.TryEnrich(Environment.ProcessId, out var enrichment);

            ok.ShouldBeTrue();
            enrichment.Path.ShouldBe(WinProcess.GetCurrentProcess().MainModule!.FileName!);
            enrichment.Architecture.ShouldNotBe(ArchitectureType.Unknown);
        }

        [Fact]
        public void StalePid_ReturnsFalseWithoutThrowing()
        {
            bool ok = Enricher.TryEnrich(GetUnusedPid(), out var enrichment);

            ok.ShouldBeFalse();
            enrichment.Path.ShouldBe(string.Empty);
        }

        private static int GetUnusedPid()
        {
            var livePids = WinProcess.GetProcesses().Select(p => p.Id).ToHashSet();
            for (int pid = 4; pid < 100_000; pid++)
            {
                if (!livePids.Contains(pid))
                {
                    return pid;
                }
            }

            throw new InvalidOperationException("Could not find an unused PID.");
        }
    }
}
```

- [ ] **Step 3: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessEnricherTests"
```

- [ ] **Step 4: Commit**

```bash
git add "TaskManager.Domain/Services/ProcessEnricher.cs" "TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessEnricherTests.cs"
git commit -m "feat(domain): stateless per-PID enricher owning path+bitness P/Invoke"
```

---

### Task 5: Single-source store + snapshot-first refresh pipeline (vertical slice)

The core rewrite. `ProcessManager`, `MainWindowViewModel`, XAML binding, DI registration, and all affected tests change together — they form one compilable deliverable.

**Files:**
- Modify: `TaskManager.Domain/Services/ProcessManager.cs` (full rewrite)
- Modify: `TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `TaskManager/ViewModels/SetPriorityWindowViewModel.cs` (PID collection type only)
- Modify: `TaskManager/Services/Factories/SetPriorityVVmFactory.cs` (PID parameter type only)
- Modify: `TaskManager/UI/Views/MainWindow.xaml` (line ~80 binding)
- Modify: `TaskManager/App.xaml.cs` (DI registrations)
- Modify: `TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs` (rework harness + new pipeline/guard/export-snapshot-shape tests)

**Interfaces:**
- Consumes: `ISystemProcessEnumerator.Capture()` → `IReadOnlyList<ProcessSnapshot>` (Task 1); `ProcessDiffEngine.Compute(...)` → `ProcessListDiff` (Task 3); `ProcessEnricher.TryEnrich(int, out ProcessEnrichment)` (Task 4); observable `Process` setters raising notifications (Task 2); existing `IDispatcherService.Invoke(Action)`, `ISettingsService.Changed`, `TimerManager.Elapsed`.
- Produces (final `ProcessManager` public surface):
  ```csharp
  public class ProcessManager : INotifyPropertyChanged
  {
      public ReadOnlyObservableCollection<ProcessItem> Items { get; }
      public int ProcessCount { get; }                 // raises PropertyChanged(nameof(ProcessCount))
      public event PropertyChangedEventHandler? PropertyChanged;

      public Task LoadProcesses();                     // initial fill == first pipeline run
      public Task PerformRefresh(bool isUserInitiated);// manual: waits on gate; polling path skips instead
      internal Task SafePollingRefreshAsync();         // timer callback wrapper, never throws
      public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> selectedPids);
      public ProcessOpSummary SetPriority(IReadOnlyCollection<int> selectedPids, ProcessPriorityClass priority);
      public IReadOnlyList<Process> SnapshotForExport();// materialized copy, safe to hold forever
  }
  ```
  Constructor signature: `ProcessManager(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator, ProcessEnricher enricher, ISettingsService settings, TimerManager timerManager, ILogger<ProcessManager> logger)`.
  `MainWindowViewModel` produces: getter-only `Processes` (the pass-through property name the XAML binds), `ProcessCount` forwarded via PM's `PropertyChanged`.

- [ ] **Step 1: Register dependencies**

In `TaskManager/App.xaml.cs`, inside `ConfigureServices`, next to the other singleton services:

```csharp
services.AddSingleton<ISystemProcessEnumerator, NtSystemProcessEnumerator>();
services.AddSingleton<ProcessEnricher>();
```

Add `using TaskManager.Domain.Abstractions;` if not already present (it is present today).

- [ ] **Step 2: Rewrite `ProcessManager.cs`**

Full replacement content:

```csharp
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services
{
    /// <summary>
    /// Single source of truth for the running-process list.
    /// Storage: ObservableCollection wrapped read-only + PID index + once-per-PID enrichment cache.
    /// Pipeline per tick: cheap snapshot → off-thread diff/enrich → ONE dispatcher batch applying
    /// removes/adds/in-place updates.
    /// </summary>
    public class ProcessManager : INotifyPropertyChanged
    {
        private readonly ObservableCollection<ProcessItem> _items = [];
        private readonly Dictionary<int, ProcessItem> _index = [];
        private readonly Dictionary<int, ProcessEnrichment> _enrichment = [];
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        public ReadOnlyObservableCollection<ProcessItem> Items { get; }

        private int _processCount;
        public int ProcessCount
        {
            get => _processCount;
            private set
            {
                if (_processCount != value)
                {
                    _processCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly IDispatcherService _dispatcher;
        private readonly ISystemProcessEnumerator _enumerator;
        private readonly ProcessEnricher _enricher;
        private readonly ISettingsService _settings;
        private readonly TimerManager _timer;
        private readonly ILogger<ProcessManager> _logger;

        public ProcessManager(IDispatcherService dispatcher, ISystemProcessEnumerator enumerator,
            ProcessEnricher enricher, ISettingsService settings, TimerManager timerManager,
            ILogger<ProcessManager> logger)
        {
            _dispatcher = dispatcher;
            _enumerator = enumerator;
            _enricher = enricher;
            _settings = settings;
            _timer = timerManager;
            _logger = logger;

            Items = new ReadOnlyObservableCollection<ProcessItem>(_items);
            Items.CollectionChanged += (_, _) => ProcessCount = _items.Count;

            _timer.Elapsed += OnProcessPolling;
            _settings.Changed += OnSettingsChanged;
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            var seconds = RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[settings.ProcessesRefreshFrequency];
            _timer.UpdatePolling(seconds);
            _logger.LogInformation("Polling interval updated to {Seconds}s", seconds);
        }

        public async Task LoadProcesses() => await RunRefreshAsync(manual: false);

        public async Task PerformRefresh(bool isUserInitiated)
        {
            await RunRefreshAsync(manual: isUserInitiated);

            if (isUserInitiated)
            {
                _timer.Restart();
            }
        }

        private async void OnProcessPolling(object? sender, System.Timers.ElapsedEventArgs e)
        {
            await SafePollingRefreshAsync();
        }

        /// <remarks>
        /// async void callback boundary: an escaping exception would terminate the process.
        /// Polling skips a tick when a refresh is already in flight; manual refresh waits.
        /// </remarks>
        internal async Task SafePollingRefreshAsync()
        {
            if (!_refreshGate.Wait(0))
            {
                _logger.LogDebug("Refresh skipped; previous refresh still in flight");
                return;
            }

            try
            {
                await RunRefreshCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Polling refresh failed");
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private async Task RunRefreshAsync(bool manual)
        {
            if (manual)
            {
                await _refreshGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await RunRefreshCoreAsync().ConfigureAwait(false);
                }
                finally
                {
                    _refreshGate.Release();
                }
            }
            else
            {
                await SafePollingRefreshAsync();
            }
        }

        private async Task RunRefreshCoreAsync()
        {
            IReadOnlyList<ProcessSnapshot> snapshot = await Task.Run(() => _enumerator.Capture()).ConfigureAwait(false);

            Dictionary<int, Process> current;
            lock (_index)
            {
                current = _index.ToDictionary(kv => kv.Key, kv => kv.Value.Process);
            }

            PipelineBatch batch = await Task.Run(() =>
            {
                var diff = ProcessDiffEngine.Compute(current, snapshot);
                var enrichments = new Dictionary<int, ProcessEnrichment>();
                lock (_index)
                {
                    foreach (var added in diff.Added)
                    {
                        if (!_enrichment.TryGetValue(added.Pid, out var e) && !_enricher.TryEnrich(added.Pid, out e))
                        {
                            e = new ProcessEnrichment(string.Empty, ArchitectureType.Unknown);
                        }
                        enrichments[added.Pid] = e;
                    }
                }

                return new PipelineBatch(diff, enrichments);
            }).ConfigureAwait(false);

            ApplyBatch(batch.Batch, batch.Enrichments);
        }

        private readonly record struct PipelineBatch(ProcessListDiff Batch, Dictionary<int, ProcessEnrichment> Enrichments);

        /// <summary>
        /// The ONLY mutation point of _items/_index/_enrichment. Runs on the UI thread.
        /// Worker threads read _index/_enrichment under lock(_index); _items is touched by
        /// no thread except the UI thread.
        /// </summary>
        private void ApplyBatch(ProcessListDiff diff, Dictionary<int, ProcessEnrichment> enrichedNew)
        {
            _dispatcher.Invoke(() =>
            {
                lock (_index)
                {
                    foreach (var pid in diff.Removed)
                    {
                        if (_index.Remove(pid, out var item))
                        {
                            _items.Remove(item);
                            _enrichment.Remove(pid);
                        }
                    }

                    foreach (var added in diff.Added)
                    {
                        var process = Materialize(added, enrichedNew[added.Pid]);
                        _enrichment[added.Pid] = enrichedNew[added.Pid];
                        var item = new ProcessItem(process);
                        _index[added.Pid] = item;
                        _items.Add(item);
                    }

                    foreach (var upd in diff.Updated)
                    {
                        if (_index.TryGetValue(upd.Pid, out var item))
                        {
                            ApplySnapshot(item.Process, upd);
                        }
                    }
                }
            });
        }

        private static Process Materialize(ProcessSnapshot s, ProcessEnrichment e) => new()
        {
            Name = s.Name,
            Pid = s.Pid,
            Path = e.Path,
            ArchitectureType = e.Architecture,
            Priority = s.BasePriority,
            ThreadCount = s.ThreadCount,
            Ppid = s.Ppid,
        };

        private static void ApplySnapshot(Process target, ProcessSnapshot s)
        {
            target.Name = s.Name;
            target.ThreadCount = s.ThreadCount;
            target.Priority = s.BasePriority;
            target.Ppid = s.Ppid;
        }

        public IReadOnlyList<Process> SnapshotForExport()
        {
            lock (_index)
            {
                return _index.Values.Select(i => CopyOf(i.Process)).ToList();
            }
        }

        private static Process CopyOf(Process p) => new()
        {
            Name = p.Name,
            Pid = p.Pid,
            Path = p.Path,
            ArchitectureType = p.ArchitectureType,
            Priority = p.Priority,
            ThreadCount = p.ThreadCount,
            Ppid = p.Ppid,
        };

        public ProcessOpSummary TerminateProcesses(IReadOnlyCollection<int> selectedProcesses)
        {
            return ExecutePerPid(selectedProcesses, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill();
                _logger.LogDebug("Process {Pid} was terminated", pid);
            });
        }

        public ProcessOpSummary SetPriority(IReadOnlyCollection<int> selectedProcesses, System.Diagnostics.ProcessPriorityClass priority)
        {
            var summary = ExecutePerPid(selectedProcesses, pid =>
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                process.PriorityClass = priority;
            });

            foreach (var pid in summary.SucceededPids)
            {
                lock (_index)
                {
                    if (_index.TryGetValue(pid, out var item))
                    {
                        item.Process.Priority = PriorityTypeHelper.GetBasePriority(priority);
                    }
                }
            }

            return summary;
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

Add `using System.Runtime.CompilerServices;` at the top (needed by `[CallerMemberName]`). Delete nothing else — the old `GetProcesses`, `TryGetProcessBitness`, `IsWow64Process` P/Invoke, `LoadProcesses` body, `Refresh`, `Processes_CollectionChanged`, and `ObservableObject` base are all replaced by the content above.

Concurrency note for reviewers: `_index` reads from worker threads (diff input, export, priority update) are guarded by `lock (_index)`; all writes happen on the UI thread inside `ApplyBatch` under the same lock discipline (UI thread takes the lock briefly inside each mutation block). `SemaphoreSlim` guarantees at most one pipeline run at a time, so worker-side reads never race a concurrent worker-side write.

- [ ] **Step 3: Simplify `MainWindowViewModel.cs`**

Changes (keep everything not listed):

1. Delete fields/regions: the settable `Processes` property (lines ~54-67), `ProcessManager_PropertyChanged` sync method (lines ~69-84).
2. Add pass-through bindings replacing them:
```csharp
        #region Bindings
        public int ProcessCount => _processManager.ProcessCount;
        public IList<DataType> DataTypes => Enum.GetValues<DataType>();

        public ReadOnlyObservableCollection<ProcessItem> Processes => _processManager.Items;
        #endregion
```
3. In the constructor: delete the line subscribing `_processManager.PropertyChanged += ProcessManager_PropertyChanged;` and delete the line `_processManager.Processes.CollectionChanged += _processManager.Processes_CollectionChanged;`. Replace with a single count forwarder:
```csharp
            _processManager.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessManager.ProcessCount))
                {
                    OnPropertyChanged(nameof(ProcessCount));
                }
            };
```
4. `ValidatePreconditions`: replace `if (Processes == null || !_processManager.Processes.Any(x => x.IsSelected))` with `if (!Processes.Any(x => x.IsSelected))`.
5. `SetPriority`: replace the trailing dispatcher/view-refresh block (lines ~207-211) with nothing — in-place `Priority` mutation now updates the bound cell automatically. Method ends after `setPriorityWindow.ShowDialog();`.
6. `TerminateProcesses`/`SetPriority` call sites: `GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid))` returns a lazy `IEnumerable<int>`, but the new PM signatures take `IReadOnlyCollection<int>` — materialize at both call sites:

```csharp
                var summary = _processManager.TerminateProcesses(
                    GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
```

and in `SetPriority()`:

```csharp
            SetPriorityWindow setPriorityWindow = factory.Create(
                GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)).ToArray());
```

(`SetPriorityWindowViewModel` stores the PIDs it receives and re-passes them to `_processManager.SetPriority` on confirm — see item 7.)

7. `TaskManager/ViewModels/SetPriorityWindowViewModel.cs`: change the stored field and constructor parameter from `IEnumerable<int>` to `IReadOnlyCollection<int>` (two lines; the body is unchanged — `_processManager.SetPriority(_processIds, ...)` now matches the new PM signature directly). In `TaskManager/Services/Factories/SetPriorityVVmFactory.cs`, change `Create(IEnumerable<int> processes)` to `Create(IReadOnlyCollection<int> processes)`.
8. `GetSelectedProcesses()` body: `_processManager.Processes` no longer exists — point it at the pass-through:
```csharp
        private IEnumerable<ProcessItem> GetSelectedProcesses() => Processes.Where(p => p.IsSelected);
```
9. Remove now-unused usings if flagged: keep `System.Collections.ObjectModel` (needed for `ReadOnlyObservableCollection`), remove `System.Windows.Data`.

- [ ] **Step 4: Fix the XAML binding**

In `TaskManager/UI/Views/MainWindow.xaml` line ~80, change:

```xml
<controls:BetterDataGrid IsReadOnly="True" AutoGenerateColumns="False" ItemsSource="{Binding Processes, Mode=TwoWay}" Margin="-5,0,0,0"
```

to:

```xml
<controls:BetterDataGrid IsReadOnly="True" AutoGenerateColumns="False" ItemsSource="{Binding Processes}" Margin="-5,0,0,0"
```

(The property instance never gets replaced after startup, so default `OneWay` is correct; item mutations flow through `INotifyCollectionChanged`.)

- [ ] **Step 5: Rework and extend `ProcessManagerTests`**

Rewrite `TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs`. Full replacement content (keeps every existing behavioral contract; adds pipeline, guard, and export-snapshot tests; uses a scripted fake enumerator so OS state doesn't drive assertions):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using WinProcess = System.Diagnostics.Process;
using WinProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;

namespace TaskManager.Tests
{
    /// <summary>
    /// Behavior contract of ProcessManager: snapshot-driven refresh batches (add/update/remove,
    /// in-place instance reuse), reentrancy guards, batch ops reporting failures as data, and
    /// materialized export snapshots immune to later store changes.
    /// A scripted enumerator drives the pipeline; the real OS process is used as the safely
    /// mutable target for batch operations.
    /// </summary>
    public class ProcessManagerTests : IDisposable
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly ProcessManager _manager;
        private readonly ISettingsService _settings;
        private readonly TimerManager _timer;
        private readonly WinProcess _self = WinProcess.GetCurrentProcess();
        private readonly ProcessPriorityClass _originalPriority;
        private readonly IDispatcherService _inlineDispatcher;

        public ProcessManagerTests()
        {
            _settings = Substitute.For<ISettingsService>();
            _settings.Current.Returns(new AppSettings
            {
                Language = "English",
                ProcessesRefreshFrequency = RefreshFrequencyType.Low, // timer is never started
                DateTimeFormat = AppSettings.Defaults.DateTimeFormat
            });

            _inlineDispatcher = Substitute.For<IDispatcherService>();
            _inlineDispatcher.When(d => d.Invoke(Arg.Any<Action>()))
                .Do(ci => ((Action)ci[0])());

            _timer = new TimerManager(_settings);
            _manager = new ProcessManager(
                _inlineDispatcher,
                _enumerator,
                new ProcessEnricher(NullLogger<ProcessEnricher>.Instance),
                _settings,
                _timer,
                NullLogger<ProcessManager>.Instance);
            _originalPriority = _self.PriorityClass;
        }

        public void Dispose() => _self.PriorityClass = _originalPriority;

        // ---- refresh pipeline ----

        [Fact]
        public async Task FirstRefresh_AddsEverythingFromSnapshot()
        {
            _enumerator.Queue(Snap(1, "alpha"), Snap(2, "beta"));

            await _manager.LoadProcesses();

            _manager.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 1, 2 });
            _manager.ProcessCount.ShouldBe(2);
        }

        [Fact]
        public async Task SecondRefresh_UpdatesExistingInstance_InPlace_AndRaisesFieldNotification()
        {
            _enumerator.Queue(Snap(1, "alpha", threadCount: 3));
            await _manager.LoadProcesses();
            var original = _manager.Items.Single();
            var notified = new List<string>();
            original.Process.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            _enumerator.Queue(Snap(1, "alpha", threadCount: 7));
            await _manager.PerformRefresh(isUserInitiated: false);

            _manager.Items.ShouldHaveSingleItem().ShouldBe(original); // same instance -> selection survives
            original.Process.ThreadCount.ShouldBe(7);
            notified.ShouldContain(nameof(Process.ThreadCount));
        }

        [Fact]
        public async Task SecondRefresh_RemovesVanished_AndAddsNew()
        {
            _enumerator.Queue(Snap(1), Snap(2));
            await _manager.LoadProcesses();

            _enumerator.Queue(Snap(2), Snap(3));
            await _manager.PerformRefresh(isUserInitiated: false);

            _manager.Items.Select(i => i.Process.Pid).ShouldBe(new int?[] { 2, 3 });
        }

        [Fact]
        public async Task RemovedPid_EvictsEnrichmentCacheEntry()
        {
            _enumerator.Queue(Snap(1));
            await _manager.LoadProcesses();
            _enumerator.Queue();
            await _manager.PerformRefresh(isUserInitiated: false);

            _enumerator.Queue(Snap(1)); // same PID reused by another image
            await _manager.PerformRefresh(isUserInitiated: false);

            _manager.Items.ShouldHaveSingleItem();
        }

        // ---- reentrancy guard ----

        [Fact]
        public async Task OverlappingPollingRefresh_SecondCallSkips_FirstCompletes()
        {
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            _enumerator.Queue(Array.Empty<ProcessSnapshot>);

            var first = _manager.SafePollingRefreshAsync();
            var second = _manager.SafePollingRefreshAsync(); // must not queue behind, must skip

            releaseFirst.SetResult();
            await first;
            second.IsCompleted.ShouldBeTrue();
            _enumerator.CallCount.ShouldBe(1); // skipped tick never captured
        }

        [Fact]
        public async Task ManualRefresh_WaitsForInFlight_ToFinish()
        {
            var releaseFirst = new TaskCompletionSource();
            _enumerator.Queue(() => releaseFirst.Task.ContinueWith(_ => Array.Empty<ProcessSnapshot>()).Result);
            _enumerator.Queue(Snap(1));

            var first = _manager.SafePollingRefreshAsync();
            var manual = _manager.PerformRefresh(isUserInitiated: true);

            manual.IsCompleted.ShouldBeFalse();
            releaseFirst.SetResult();
            await manual;

            _enumerator.CallCount.ShouldBe(2);
            _manager.ProcessCount.ShouldBe(1);
        }

        // ---- export snapshot ----

        [Fact]
        public async Task ExportSnapshot_IsImmuneToLaterStoreChanges()
        {
            _enumerator.Queue(Snap(1, "one"), Snap(2, "two"));
            await _manager.LoadProcesses();

            var snapshot = _manager.SnapshotForExport();

            _enumerator.Queue();
            await _manager.PerformRefresh(isUserInitiated: false); // everything exits

            _manager.Items.ShouldBeEmpty();
            snapshot.Count.ShouldBe(2);
            snapshot.Single(p => p.Pid == 1).Name.ShouldBe("one");
        }

        // ---- batch operations (existing contracts preserved) ----

        [Fact]
        public void SettingsChanged_AppliesNewPollingIntervalImmediately()
        {
            _timer.Interval.ShouldBe(10_000); // Low => 10s, from Current snapshot

            var high = AppSettings.Defaults with
            {
                ProcessesRefreshFrequency = RefreshFrequencyType.High
            };
            _settings.Changed += Raise.Event<Action<AppSettings>>(high);

            _timer.Interval.ShouldBe(5_000); // High => 5s
        }

        [Fact]
        public async Task SetPriority_UpdatesRealProcessAndStoredModel()
        {
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            _manager.SetPriority(new[] { _self.Id }, ProcessPriorityClass.AboveNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
            _manager.Items.Single().Process.Priority.ShouldBe(10); // AboveNormal => base priority 10
        }

        [Fact]
        public async Task SetPriority_StalePid_DoesNotAbortRemainingUpdates()
        {
            int stalePid = GetUnusedPid();
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            // a process can die between selection and confirmation; the rest of the batch must survive it
            _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.BelowNormal);

            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.BelowNormal);
            _manager.Items.Single().Process.Priority.ShouldBe(6); // BelowNormal => base priority 6
        }

        [Fact]
        public async Task SetPriority_StalePid_IsReportedInSummary()
        {
            int stalePid = GetUnusedPid();

            var summary = _manager.SetPriority(new[] { stalePid }, ProcessPriorityClass.Normal);

            summary.SucceededPids.ShouldBeEmpty();
            var failure = summary.Failures.ShouldHaveSingleItem();
            failure.Pid.ShouldBe(stalePid);
            failure.Reason.ShouldBe(ProcessOpFailureReason.ProcessExited);
        }

        [Fact]
        public async Task SetPriority_MixedBatch_ReportsBothOutcomesAndSurvives()
        {
            int stalePid = GetUnusedPid();
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();

            var summary = _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.AboveNormal);

            summary.SucceededPids.ShouldBe(new[] { _self.Id });
            summary.Failures.ShouldHaveSingleItem();
            _self.Refresh();
            _self.PriorityClass.ShouldBe(ProcessPriorityClass.AboveNormal);
        }

        [Fact]
        public async Task TerminateProcesses_KillsTarget_AndReportsStalePidWithoutAborting()
        {
            _enumerator.Queue(SelfSnap());
            await _manager.LoadProcesses();
            using var victim = WinProcess.Start(new WinProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping -n 30 127.0.0.1 > nul",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            victim.ShouldNotBeNull();
            int stalePid = GetUnusedPid();

            try
            {
                var summary = _manager.TerminateProcesses(new[] { victim.Id, stalePid });

                summary.SucceededPids.ShouldBe(new[] { victim.Id });
                var failure = summary.Failures.ShouldHaveSingleItem();
                failure.Pid.ShouldBe(stalePid);
                victim.WaitForExit(5_000).ShouldBeTrue();
                victim.HasExited.ShouldBeTrue();
            }
            finally
            {
                if (!victim.HasExited)
                {
                    try { victim.Kill(); } catch { /* best-effort cleanup */ }
                }
            }
        }

        [Fact]
        public async Task SafePollingRefreshAsync_SwallowsAndLogsUnexpectedFailures()
        {
            var throwingDispatcher = Substitute.For<IDispatcherService>();
            throwingDispatcher
                .When(d => d.Invoke(Arg.Any<Action>()))
                .Do(_ => throw new InvalidOperationException("dispatcher died"));
            var failingManager = new ProcessManager(
                throwingDispatcher,
                ScriptedEnumerator.Of(SelfSnap()),
                new ProcessEnricher(NullLogger<ProcessEnricher>.Instance),
                _settings,
                new TimerManager(_settings),
                NullLogger<ProcessManager>.Instance);

            await failingManager.SafePollingRefreshAsync();
        }

        [Fact]
        public async Task EnumeratorFailure_IsSwallowedByPollingWrapper()
        {
            _enumerator.ThrowNext(new InvalidOperationException("snapshot boom"));

            await _manager.SafePollingRefreshAsync();

            _manager.Items.ShouldBeEmpty();
        }

        // ---- helpers ----

        private static ProcessSnapshot SelfSnap() =>
            new(Environment.ProcessId, "self", threadCount: 1, ppid: null, basePriority: 8);

        private static ProcessSnapshot Snap(int pid, string name = "n", int threadCount = 1) =>
            new(pid, name, threadCount, ppid: 4, basePriority: 8);

        private static int GetUnusedPid()
        {
            var livePids = WinProcess.GetProcesses().Select(p => p.Id).ToHashSet();
            for (int pid = 4; pid < 100_000; pid++)
            {
                if (!livePids.Contains(pid))
                {
                    return pid;
                }
            }

            throw new InvalidOperationException("Could not find an unused PID.");
        }

        private sealed class ScriptedEnumerator : ISystemProcessEnumerator
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
    }
}
```

Implementation note for the two concurrency-scripted tests: `releaseFirst.Task.ContinueWith(...).Result` blocks the capture call until released, simulating a long-running refresh deterministically. If `SafePollingRefreshAsync`'s skip semantics make `OverlappingPollingRefresh_SecondCallSkips_FirstCompletes` flaky in practice (both calls racing before the gate is taken), serialize deterministically instead: start `first`, poll `_manager` internals is NOT possible — acceptable alternative: assert only `second.IsCompleted` immediately and `CallCount == 1` after awaiting `first`, dropping the strict ordering claim about which call took the gate (rename test accordingly if adjusted).

Also delete the now-obsolete `GivenTrackedProcess` helper (replaced by enumerator seeding) — it does not exist in the replacement content above.

- [ ] **Step 6: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessManagerTests"
dotnet test TaskManager.Tests --filter "FullyQualifiedName~Viewmodels"
dotnet test TaskManager.Tests
```

Full suite must be green (UI control tests exercise `BetterDataGrid` against its own hosts and are unaffected, but run everything anyway).

Manual smoke check (optional but recommended): launch the app (`dotnet run --project TaskManager`), confirm the grid populates, rows' thread counts change over ticks, terminating a process makes its row disappear within one interval, and selection survives a refresh tick.

- [ ] **Step 7: Commit**

```bash
git add "TaskManager.Domain/Services/ProcessManager.cs" "TaskManager/ViewModels/MainWindowViewModel.cs" "TaskManager/UI/Views/MainWindow.xaml" "TaskManager/App.xaml.cs" "TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs"
git commit -m "refactor(domain,ui): single-source process store with snapshot-first refresh pipeline"
```

---

### Task 6: Wire export to materialized snapshots

**Files:**
- Modify: `TaskManager/ViewModels/DataExportWindowViewModel.cs`
- Modify: `TaskManager/ViewModels/MainWindowViewModel.cs` (only the `Export()` method body)
- Test: `TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `ProcessManager.SnapshotForExport()` → `IReadOnlyList<Process>` (Task 5).
- Produces: `DataExportWindowViewModel` constructor accepts `IReadOnlyList<Process> processes`; `DataExportViewModelFactory.Create` signature stays `Create(IEnumerable<Process>)` (compatible — `IReadOnlyList<Process>` is an `IEnumerable<Process>`).

- [ ] **Step 1: Change the export VM to hold the snapshot**

In `TaskManager/ViewModels/DataExportWindowViewModel.cs`:

Replace:
```csharp
		private IEnumerable<Process> processes;
```
with:
```csharp
		private readonly IReadOnlyList<Process> _processes;
```

Replace the constructor parameter and assignment:
```csharp
		public DataExportWindowViewModel(IServiceProvider serviceProvider,
			ISettingsService settings,
			IMessageService messageService,
			IErrorHandler errorHandler,
			IReadOnlyList<Process> processes)
		{
            _serviceProvider = serviceProvider;
            _settings = settings;
			_messageService = messageService;
			_errorHandler = errorHandler;
            _processes = processes;
```

And in `TryExport` replace `exporter.Export(DirPath, processes)` with `exporter.Export(DirPath, _processes)`.

- [ ] **Step 2: Capture the snapshot in MainWindowViewModel.Export**

In `MainWindowViewModel.Export()` replace:

```csharp
            var processes = _processManager.Processes.Select(x => x.Process);
```

with:

```csharp
            var processes = _processManager.SnapshotForExport();
```

No factory signature change needed.

- [ ] **Step 3: Extend the export VM tests**

In `TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs`, add this test inside `DataExportWindowViewModelTests` (it reuses the class's `_serviceProvider`, `_messageService`, `_exporterFactory`, and `_tempDirectory` scaffolding; note the existing constructor call in the test class passes `[]`, which still compiles against the new `IReadOnlyList<Process>` parameter):

```csharp
        [Fact]
        public void Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations()
        {
            var processes = new List<Process>
            {
                new() { Name = "p1", Pid = 1, Path = string.Empty },
                new() { Name = "p2", Pid = 2, Path = string.Empty },
            };
            var exporter = new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance);
            _exporterFactory.CreateDataExporter(DataTypeEnum.Txt).Returns(exporter);
            var vm = new DataExportWindowViewModel(
                _serviceProvider,
                Substitute.For<ISettingsService>(),
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                processes);
            vm.DirPath = _tempDirectory;
            vm.Exportation = ExportationTypeEnum.Processes;
            vm.DataType = DataTypeEnum.Txt;

            processes.Clear(); // caller-side mutation after handoff must not leak into the dialog

            vm.TryExport(ExportationTypeEnum.Processes, DataTypeEnum.Txt).ShouldBeTrue();

            var written = File.ReadAllLines(Directory.GetFiles(_tempDirectory, "record-*").Single());
            written.Count(line => line.Contains("p1")).ShouldBe(1);
            written.Count(line => line.Contains("p2")).ShouldBe(1);
        }
```

`Exportation` and `DataType` settable properties already exist on the VM; `NewSettings()` helper already exists in the file.

- [ ] **Step 4: Run validation**

```bash
dotnet build TaskManager.slnx
dotnet test TaskManager.Tests --filter "FullyQualifiedName~DataExportWindowViewModelTests"
dotnet test TaskManager.Tests
```

- [ ] **Step 5: Commit**

```bash
git add "TaskManager/ViewModels/DataExportWindowViewModel.cs" "TaskManager/ViewModels/MainWindowViewModel.cs" "TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs"
git commit -m "refactor(ui): export consumes materialized process snapshots"
```

---

### Task 7: End-to-end verification and spec traceability sweep

**Files:**
- No source changes expected; fix-forward if gaps found.

**Interfaces:**
- Consumes: everything above.
- Produces: verified, green solution matching the spec.

- [ ] **Step 1: Full clean validation**

```bash
dotnet build TaskManager.slnx --no-incremental
dotnet test TaskManager.Tests
```

- [ ] **Step 2: Manual acceptance pass**

Launch `dotnet run --project TaskManager` and verify:
1. Grid populates shortly after launch (initial load runs the same pipeline).
2. Row data goes live: pick a visible app whose thread count fluctuates (or start/stop a `ping -n 60` child) — the change appears within one refresh interval without the row being recreated.
3. Terminating a process removes its row within one interval.
4. Checkbox-selected rows stay selected across multiple refresh ticks.
5. Export writes a file containing the rows visible at click time.
6. Status bar count matches grid rows at all times.

- [ ] **Step 3: Spec traceability checklist**

Confirm each spec section has landed:
- Single source of truth / no settable Processes on VM or PM — Task 5 Steps 2-3.
- `ProcessCount` from `CollectionChanged`, no double subscription — Task 5 Step 2.
- `Process` raises per-field notifications — Task 2.
- Snapshot-first enumeration via seam — Tasks 1, 5.
- O(n) pure diff — Task 3.
- New-PID-only enrichment, cached forever, evicted on exit — Tasks 4, 5.
- One dispatcher marshal per refresh; single writer — Task 5 Step 2 (`ApplyBatch`).
- Reentrancy: polling skips, manual waits — Task 5 Steps 2 & 5.
- Export snapshot immutability — Tasks 5 & 6.
- Protected processes visible with degraded data — covered implicitly by snapshot-first design (they appear with empty path); spot-check manually that `System`/`Registry` appear in the grid.
- Selection survives refresh — Task 5 Step 5 (`SecondRefresh_UpdatesExistingInstance_InPlace...`).

Fix any gap found, rerun validation, and commit fixes:

```bash
git add -A
git commit -m "fix: close spec-traceability gaps found in end-to-end sweep"
```

(Only run the commit if there were actual changes.)
