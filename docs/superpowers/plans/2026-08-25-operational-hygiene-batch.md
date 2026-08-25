# Operational Hygiene Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land five verified findings: resx template cleanup, config-driven log floor, honest PID typing, async export path, self-healing polling loop.

**Architecture:** All five are localized changes with no cross-coupling except that the export VM's tests are touched by both PID typing (indirectly) and async export (directly) — ordered so typing lands first. The polling loop gains an outer retry shell while preserving every semantic the existing FakeTimeProvider suite encodes.

**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm (`AsyncRelayCommand`), BCL `TimeProvider`/`PeriodicTimer`, xunit.v3 + Shouldly + NSubstitute + Xunit.StaFact.

## Global Constraints

- Build: `dotnet build TaskManager.slnx`; hermetic suite: `dotnet test tests/TaskManager.UnitTests`; live suite: `dotnet test tests/TaskManager.IntegrationTests`.
- Env knob name is exactly `TASKMANAGER_LOGLEVEL`; invalid/absent values fall back to `Debug` when a debugger is attached, else `Information`.
- The polling error-backoff (`Task.Delay(TimeSpan.FromSeconds(1))`) deliberately does NOT take the cancellation token — shutdown may linger up to 1s; do not "fix" this.
- Exporters run on worker threads safely: they are stateless per call and read `settings.Current` during export.
- `Strings.Designer.cs` contains NO properties for the template junk names (`Name1`, `Color1`, `Bitmap1`, `Icon1`) — verified; deleting the resx blocks requires no Designer edits.
- Commit style: `refactor:` / `feat:` / `test:` prefixes, imperative mood.

---

### Task 1: resx template cleanup

**Files:**
- Modify: `src/TaskManager/Resources/Languages/Strings.resx` (template block, ~lines 20–27)
- Modify: `src/TaskManager/Resources/Languages/Strings.pl.resx` (identical block)

**Interfaces:**
- Produces: nothing consumed elsewhere — the deleted names have zero references in `Strings.Designer.cs`.

- [ ] **Step 1: Delete the template data blocks**

In BOTH files, delete these elements (they sit immediately after the four `resheader` rows, before the instructional comment block — delete ONLY the `<data>` elements, keep the surrounding comments):

```xml
    <data name="Name1"><value>this is my long string</value><comment>this is a comment</comment></data>
    <data name="Color1" type="System.Drawing.Color, System.Drawing">Blue</data>
    <data name="Bitmap1" mimetype="application/x-microsoft.net.object.binary.base64">
        <value>[base64 mime encoded serialized .NET Framework object]</value>
    </data>
    <data name="Icon1" type="System.Drawing.Icon, System.Drawing" mimetype="application/x-microsoft.net.object.bytearray.base64">
        <value>[base64 mime encoded string representing a byte array form of the .NET Framework object]</value>
        <comment>This is a comment</comment>
    </data>
```

- [ ] **Step 2: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Then:

```bash
rg -n "Name1|Bitmap1|Icon1|\"Color1\"" src --glob "*.resx"
# expect: no output
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: drop Visual Studio resx template junk"
```

---

### Task 2: Config-driven log floor

**Files:**
- Modify: `src/TaskManager/App.xaml.cs`
- Test (create): `tests/TaskManager.UnitTests/AppStartup/LogFloorTests.cs`

**Interfaces:**
- Produces: `internal static bool App.TryResolveMinimumLogLevel(string? raw, out LogLevel level)` — pure, culture-invariant parse used by `ConfigureServices`.

- [ ] **Step 1: Implement the resolver and wire it**

In `App.xaml.cs`, add `using System.Diagnostics;` at the top, then replace the logging setup:

```csharp
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(ResolveMinimumLogLevel());
                logging.AddProvider(new FileLoggerProvider());
            });
```

and add two members:

```csharp
        /// <summary>
        /// TASKMANAGER_LOGLEVEL overrides; otherwise Debug under a debugger, else Information.
        /// </summary>
        internal static LogLevel ResolveMinimumLogLevel() =>
            TryResolveMinimumLogLevel(Environment.GetEnvironmentVariable("TASKMANAGER_LOGLEVEL"), out var parsed)
                ? parsed
                : Debugger.IsAttached ? LogLevel.Debug : LogLevel.Information;

        internal static bool TryResolveMinimumLogLevel(string? raw, out LogLevel level)
        {
            return Enum.TryParse(raw, ignoreCase: true, out level)
                   && Enum.IsDefined(level);
        }
```

(`Enum.TryParse<LogLevel>` with `out level` infers the generic from the parameter; if your compiler complains, write `Enum.TryParse<LogLevel>(raw, ignoreCase: true, out level)`.)

- [ ] **Step 2: Add the parse tests**

Create `tests/TaskManager.UnitTests/AppStartup/LogFloorTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TaskManager.UnitTests.AppStartup
{
    /// <summary>Pure parse contract of the TASKMANAGER_LOGLEVEL override.</summary>
    public class LogFloorTests
    {
        [Theory]
        [InlineData("Information", LogLevel.Information)]
        [InlineData("debug", LogLevel.Debug)]
        [InlineData("Warning", LogLevel.Warning)]
        public void TryResolve_KnownNames_ParseCaseInsensitively(string raw, LogLevel expected)
        {
            App.TryResolveMinimumLogLevel(raw, out var level).ShouldBeTrue();
            level.ShouldBe(expected);
        }

        [Theory]
        [InlineData("verbose")]
        [InlineData("")]
        [InlineData(null)]
        public void TryResolve_UnknownOrMissing_FailsWithoutThrowing(string? raw)
        {
            App.TryResolveMinimumLogLevel(raw, out var level).ShouldBeFalse();
            level.ShouldBe(default(LogLevel)); // None — callers must not use it
        }
    }
}
```

Note: `App` is a WPF `Application` subclass but `TryResolveMinimumLogLevel` touches no instance state — calling it statically in a unit test is safe on any thread.

- [ ] **Step 3: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: TASKMANAGER_LOGLEVEL override replaces hardcoded debug logging"
```

---

### Task 3: Honest PID typing

**Files:**
- Modify: `src/TaskManager.Domain/Models/Process.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (one line)

**Interfaces:**
- Produces: `Process.Pid` is `int` (was `int?`). No other signatures change.

- [ ] **Step 1: Retype the property**

In `Process.cs`, change:

```csharp
        [ObservableProperty]
        private int? _pid;
```

to:

```csharp
        [ObservableProperty]
        private int _pid;
```

- [ ] **Step 2: Simplify the one conversion site**

In `MainWindowViewModel.cs`:

```csharp
        private int[] GetSelectedPids() =>
            GetSelectedProcesses().Select(x => x.Process.Pid).ToArray();
```

- [ ] **Step 3: Sweep for stragglers**

```bash
rg -n "Convert.ToInt32|Pid\.HasValue|Pid == null|int\? Pid|int\? _pid" src tests --glob "*.cs"
# expect: no output
```

Fix any hit the sweep reveals by dropping the null-dance (all construction sites already assign real PIDs).

- [ ] **Step 4: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: Process.Pid is a plain int; snapshots always carry one"
```

---

### Task 4: Async export path

**Files:**
- Modify: `src/TaskManager/Services/ErrorHandling/ErrorHandlerExtensions.cs` (add generic async guard)
- Modify: `src/TaskManager/ViewModels/DataExportWindowViewModel.cs`
- Modify: `tests/TaskManager.UnitTests/ViewModels/DataExportWindowViewModelTests.cs`
- Modify: `tests/TaskManager.UnitTests/Services/WindowServiceTests.cs`

**Interfaces:**
- Consumes: exporter factory delegate `Func<DataType, BaseDataExporter>` (unchanged); `BaseDataExporter.Export<T>` returning `ExportResult` (unchanged).
- Produces: `ErrorHandlerExtensions.GuardAsync<T>(this IErrorHandler, Func<Task<T>>, string operationContext) : Task<T>` and `DataExportWindowViewModel.TryExportAsync(DataType) : Task<bool>`; sync `TryExport` deleted.

- [ ] **Step 1: Add the generic async guard**

In `ErrorHandlerExtensions.cs`, append beside the existing `Guard<T>`:

```csharp
        public static async Task<T> GuardAsync<T>(this IErrorHandler errorHandler, Func<Task<T>> operation,
            [CallerMemberName] string operationContext = "")
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                errorHandler.Handle(ex, operationContext);
                return default!;
            }
        }
```

- [ ] **Step 2: Convert the ViewModel**

In `DataExportWindowViewModel.cs`:

Constructor line:

```csharp
            OnConfirmClick = new AsyncRelayCommand(OnConfirmAsync);
```

Property declaration:

```csharp
        public AsyncRelayCommand OnConfirmClick { get; }
```

Replace `OnConfirm` and `TryExport`:

```csharp
        private async Task OnConfirmAsync()
        {
            await _errorHandler.GuardAsync(async () =>
            {
                if (Exportation is not ExportationType || DataType is not DataType dataType)
                {
                    _messageService.ShowMessage(Strings.SelectOptionsRequired, Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!await TryExportAsync(dataType))
                {
                    return; // failure already reported; keep the window open for a corrected attempt
                }

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "exporting process data");
        }

        internal async Task<bool> TryExportAsync(DataType dataType)
        {
            return await _errorHandler.GuardAsync(async () =>
            {
                // exporters are stateless per call; ClosedXML workbooks can take seconds,
                // so the file generation runs off the UI thread
                var result = await Task.Run(() => _exporterFactory(dataType).Export(DirPath, _processes));
                if (result.IsSuccess)
                {
                    return true;
                }

                _messageService.ShowMessage(
                    string.Format(Strings.ExportFailedFormat, DirPath) + " " + DescribeFailure(result.FailureReason!.Value),
                    Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }, "exporting process data");
        }
```

Keep `using System.Windows.Input;` (`SelectFolderCommand` remains a `RelayCommand`). Delete the old `TryExport` and `OnConfirm`.

- [ ] **Step 3: Update the export VM tests**

In `DataExportWindowViewModelTests.cs`, five conversions — bodies otherwise unchanged:

- `TryExport_Success_WritesFileAndReturnsTrue`, `TryExport_Failure_ShowsSingleMessageAndReturnsFalse`, `TryExport_UnexpectedFactoryCrash_IsGuardedAndReturnsFalse`, `Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations`: signature → `async Task`; call site → `await _viewModel.TryExportAsync(DataTypeEnum.Txt)` / `await vm.TryExportAsync(DataTypeEnum.Txt)`.
- `OnConfirm_MissingOptions_ShowsError_DoesNotClose`: signature → `async Task`; call site → `await _viewModel.OnConfirmClick.ExecuteAsync(null);` (add `using CommunityToolkit.Mvvm.Input;` only if the compiler wants it — `ExecuteAsync` is an interface member of `IAsyncRelayCommand` reachable via the concrete type without extra usings).

- [ ] **Step 4: Update the WindowService export test**

In `WindowServiceTests.cs`, `CreateExportDialog_SeedsWithMaterializedProcesses`: signature → `async Task`; call site →

```csharp
                (await viewModel.TryExportAsync(DataTypeEnum.Txt)).ShouldBeTrue();
```

- [ ] **Step 5: Validate**

Run: `dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests`. Sweep:

```bash
rg -n "internal bool TryExport|void OnConfirm\b" src --glob "*.cs"
# expect: no output
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: exports run off the UI thread like kills and priority writes"
```

---

### Task 5: Self-healing polling loop

**Files:**
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs` (single method body)

**Interfaces:**
- Produces: none externally visible — `RunPollingLoopAsync` keeps its signature; only failure behavior changes.

- [ ] **Step 1: Restructure the loop**

Replace the entire `RunPollingLoopAsync` method:

```csharp
        private async Task RunPollingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var seconds = CurrentIntervalSeconds;
                    if (seconds == 0)
                    {
                        return; // paused: idle until the next settings change starts a fresh loop
                    }

                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), _timeProvider);
                    while (await timer.WaitForNextTickAsync(ct))
                    {
                        await SafePollingRefreshAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    return; // normal reconfiguration/shutdown path
                }
                catch (Exception ex)
                {
                    // defense in depth: a single unexpected failure must not kill polling
                    // until the next settings change. Real-time backoff on purpose.
                    _logger.LogError(ex, "Polling iteration failed; restarting loop");
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }
```

- [ ] **Step 2: Validate**

The existing polling suite encodes the preserved semantics and must pass UNCHANGED:

```bash
dotnet build TaskManager.slnx && dotnet test tests/TaskManager.UnitTests --filter-class "TaskManager.UnitTests.Presentation.ProcessListCatalogPollingTests"
dotnet test tests/TaskManager.UnitTests
```

Run the full hermetic suite twice consecutively.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "fix: polling loop survives unexpected failures instead of dying silently"
```

---

### Task 6: Full verification sweep

**Files:**
- None (verification only)

- [ ] **Step 1: Build + suites**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests   # run twice consecutively
dotnet test tests/TaskManager.IntegrationTests
```

All green required.

- [ ] **Step 2: Boundary greps**

```bash
rg -n "Convert.ToInt32|int\? _pid|internal bool TryExport|Name1|Bitmap1" src tests --glob "*.cs" --glob "*.resx"
# expect: no output

rg -n "SetMinimumLevel" src/TaskManager/App.xaml.cs
# expect: exactly one hit, calling ResolveMinimumLogLevel()
```

- [ ] **Step 3: Report**

Summarize suites, grep results, deviations. Commit nothing unless something changed.

---
