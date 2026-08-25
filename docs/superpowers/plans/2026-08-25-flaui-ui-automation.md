# FlaUI UI-Automation Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A FlaUI-driven E2E suite (10 behavior tests over the real exe) that structurally cannot run during default builds/tests — CI-only, with deliberate manual opt-in.

**Architecture:** New out-of-solution test project launches the real exe via FlaUI with three environment redirects (settings dir, log dir, instance-name suffix) honored by small production hooks; page objects expose behavioral lookups (titles, visible labels from the app's own `Strings` resource, process liveness); a collection fixture owns exactly one app session per run.

**Tech Stack:** FlaUI.UIA3, xunit.v3/MTP, Shouldly, NSubstitute-free (real processes), GitHub Actions windows-latest. One production csproj gains `AllowUnsafeBlocks`? **No** — zero package changes outside the new project; the exe gains three env-var reads, a mutex-suffix parameter, and a real window title.

**Spec:** `docs/superpowers/specs/2026-08-25-flaui-ui-automation-testing-design.md` (authoritative; includes venue decision and not-tested list).

## Global Constraints

- Branch `build/engineering-guardrails` (verify; never switch/push). Main-suite baseline: **168 tests green**, zero warnings (`TreatWarningsAsErrors`).
- **GUI policy (README Development notes): automated validation must never open windows on the developer PC.** The ONLY permitted GUI-launching code lives in this new out-of-solution project; do NOT add app-launch steps to any other task, and do NOT execute the app interactively while implementing Tasks 1–3. Task 4's CI step is where windows run (on the runner).
- Zero packages outside `tests/TaskManager.UiAutomationTests/`; all versions via `Directory.Packages.props`.
- All user-visible strings referenced from `TaskManager.Resources.Languages.Strings` (project reference gives access); never hardcode English literals except the fixed window title `"Task Manager"` and OS-standard `Enter`/`Escape` keys.
- Mutex naming: base `Local\TaskManager.SingleInstance`; optional suffix appended as `. <suffix>` — unset env ⇒ byte-for-byte today's behavior.
- Commit messages: lowercase conventional prefixes.
- Full-suite validation: `dotnet test tests/TaskManager.UnitTests` (MTP; `--nologo` rejected by test invocations).

---

### Task 1: Production testability hooks

**Files:**
- Create: `src/TaskManager/Infrastructure/TaskManagerEnvironment.cs`
- Modify: `src/TaskManager/App.xaml.cs` (ConfigureServices logging provider, CoreServices call, OnStartup guard construction)
- Modify: `src/TaskManager/Infrastructure/Composition/CoreServicesRegistration.cs` (ISettingsStore factory)
- Modify: `src/TaskManager/Infrastructure/SingleInstanceGuard.cs` (optional instance-name suffix)
- Modify: `src/TaskManager/UI/Views/MainWindow.xaml` (`Title`)
- Test: extend `tests/TaskManager.UnitTests/Infrastructure/SingleInstanceGuardTests.cs` (one test)

**Interfaces:**
- Consumes: existing ctors `JsonSettingsStore(string directory, ILogger logger)`, `FileLoggerProvider(string logDirectory)`, `SingleInstanceGuard(bool waitForExistingRelease)`.
- Produces (consumed by Tasks 2–4):
  - Env keys centralized: `TaskManagerEnvironment.SettingsDir` = `"TASKMANAGER_SETTINGS_DIR"`, `.LogDir` = `"TASKMANAGER_LOG_DIR"`, `.InstanceName` = `"TASKMANAGER_INSTANCE_NAME"`
  - `SingleInstanceGuard(bool waitForExistingRelease, string? instanceName = null)` — mutex `Local\TaskManager.SingleInstance` or `Local\TaskManager.SingleInstance.<instanceName>`
  - Window title `"Task Manager"`

- [ ] **Step 1: Environment keys**

`src/TaskManager/Infrastructure/TaskManagerEnvironment.cs`:

```csharp
namespace TaskManager.Infrastructure
{
    /// <summary>Well-known environment variables enabling test/portable redirections.</summary>
    internal static class TaskManagerEnvironment
    {
        public const string SettingsDir = "TASKMANAGER_SETTINGS_DIR";
        public const string LogDir = "TASKMANAGER_LOG_DIR";
        public const string InstanceName = "TASKMANAGER_INSTANCE_NAME";
    }
}
```

- [ ] **Step 2: Redirects in composition**

In `App.xaml.cs` `ConfigureServices(IServiceCollection services)`:

```csharp
        private void ConfigureServices(IServiceCollection services)
        {
            var logDirectory = Environment.GetEnvironmentVariable(TaskManagerEnvironment.LogDir);

            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(ResolveMinimumLogLevel());
                logging.AddProvider(string.IsNullOrEmpty(logDirectory)
                    ? new FileLoggerProvider()
                    : new FileLoggerProvider(logDirectory));
            });

            services.AddCoreServices(
                Environment.GetEnvironmentVariable(TaskManagerEnvironment.SettingsDir));

            services.AddUiServices();
        }
```

(`using TaskManager.Infrastructure;` already present.)

In `CoreServicesRegistration.AddCoreServices` — change signature and the settings-store registration:

```csharp
        public static IServiceCollection AddCoreServices(this IServiceCollection services, string? settingsDirectory)
        {
            services.AddSingleton<ISettingsStore>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<JsonSettingsStore>>();
                return string.IsNullOrEmpty(settingsDirectory)
                    ? new JsonSettingsStore(logger)
                    : new JsonSettingsStore(settingsDirectory, logger);
            });
```

(all other registrations unchanged; add `using Microsoft.Extensions.Logging;` if absent.)

- [ ] **Step 3: Mutex suffix**

`SingleInstanceGuard`: change constructor and mutex resolution —

```csharp
        private readonly string _mutexName;

        public SingleInstanceGuard(bool waitForExistingRelease, string? instanceName = null)
        {
            _mutexName = string.IsNullOrEmpty(instanceName)
                ? MutexName
                : $"{MutexName}.{instanceName}";
```

Replace EVERY subsequent use of the `MutexName` constant inside the constructor body (normal-mode `new Mutex(...)`, handoff-mode `TryOpenExisting`) with `_mutexName`. The `AbandonedMutexException`/timeout logic stays untouched.

- [ ] **Step 4: Window title**

`MainWindow.xaml` line 16: `Title="MainWindow"` → `Title="Task Manager"`.

- [ ] **Step 5: Guard-suffix test**

Append to `tests/TaskManager.UnitTests/Infrastructure/SingleInstanceGuardTests.cs` (inside the existing `[Collection("SingleInstanceGuard")]` class):

```csharp
        [Fact]
        public void SuffixedInstance_DoesNotCollide_WithUnsuffixed()
        {
            using var plain = new SingleInstanceGuard(waitForExistingRelease: false);
            using var suffixed = new SingleInstanceGuard(waitForExistingRelease: false, instanceName: "uitest");

            plain.IsFirstInstance.ShouldBeTrue();
            suffixed.IsFirstInstance.ShouldBeTrue();
        }
```

- [ ] **Step 6: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; 169/169 twice. REMEMBER THE GUI POLICY: no app launches in this task.

- [ ] **Step 7: Commit**

```bash
git add src/TaskManager tests/TaskManager.UnitTests
git commit -m "feat: environment redirects, mutex suffix and real window title"
```

---

### Task 2: UiAutomationTests project + harness + launch smoke

**Files:**
- Create: `tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj`
- Create: `tests/TaskManager.UiAutomationTests/AppSession.cs`
- Create: `tests/TaskManager.UiAutomationTests/UiCollection.cs`
- Create: `tests/TaskManager.UiAutomationTests/Pages/MainWindowPage.cs`
- Create: `tests/TaskManager.UiAutomationTests/SmokeLaunchTests.cs`
- Modify: `Directory.Packages.props` (add FlaUI version entry)

**Interfaces:**
- Consumes: Task 1's redirects + window title; app assembly reference for `Strings` and exe-path resolution.
- Produces (consumed by Task 3):
  - `AppSession` (collection fixture `UiCollection`): properties `Application App`, `UIA3Automation Automation`, `string ExePath`, `string MainWindowTitle = "Task Manager"`, methods `AutomationElement GetTopLevelWindow(string title)`, `void AssertAlive()`
  - `MainWindowPage(AutomationElement window)`: `IReadOnlyList<string> StatusTexts()`, `AutomationElement FindRowContaining(string text)`, `void SelectRow(AutomationElement row)`, `void InvokeMenu(params string[] headers)`
  - Collection name: `"ui"`

- [ ] **Step 1: Packages + project**

`Directory.Packages.props` tests group (alphabetical):

```xml
    <PackageVersion Include="FlaUI.UIA3" Version="4.0.0" />
```

If restore fails because 4.0.0 does not exist, pin latest stable via `dotnet package search FlaUI.UIA3 --exact-match` and report the version.

`tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>TaskManager.UiAutomationTests</RootNamespace>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FlaUI.UIA3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskManager\TaskManager.csproj" />
  </ItemGroup>

</Project>
```

Deliberately OUT of `TaskManager.slnx` — do not add it there.

- [ ] **Step 2: Harness**

`tests/TaskManager.UiAutomationTests/AppSession.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace TaskManager.UiAutomationTests
{
    /// <summary>
    /// One real app process for the entire run, fully isolated from the developer's
    /// machine data via environment redirects. Owns automation lifetime and teardown.
    /// </summary>
    public sealed class AppSession : IDisposable
    {
        public const string MainWindowTitle = "Task Manager";

        private readonly string _settingsDir;
        private readonly string _logDir;
        private bool _disposed;

        public Application App { get; }
        public UIA3Automation Automation { get; }
        public string ExePath { get; }
        public int ProcessId { get; }

        public AppSession()
        {
            var appAssembly = typeof(global::TaskManager.App).Assembly.Location;
            ExePath = Path.ChangeExtension(appAssembly, ".exe");
            _settingsDir = Path.Combine(Path.GetTempPath(), $"tm-uitest-settings-{Guid.NewGuid():N}");
            _logDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-test-logs");

            var instanceName = $"uitest-{Guid.NewGuid():N}";

            App = Application.Launch(ExePath, psi =>
            {
                psi.EnvironmentVariables[global::TaskManager.Infrastructure.TaskManagerEnvironment.SettingsDir] = _settingsDir;
                psi.EnvironmentVariables[global::TaskManager.Infrastructure.TaskManagerEnvironment.LogDir] = _logDir;
                psi.EnvironmentVariables[global::TaskManager.Infrastructure.TaskManagerEnvironment.InstanceName] = instanceName;
            });

            ProcessId = App.PID;

            Automation = new UIA3Automation();

            // Bounced launch = guard misfire or startup crash: fail loudly, not silently.
            var window = Retry.WhileNull(
                () => App.GetMainWindow(Automation, TimeSpan.FromMilliseconds(250)),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(250)).Result;

            window.ShouldNotBeNull("main window did not appear within 10 s");
            window.Properties.ProcessId.Value.ShouldBe(ProcessId,
                "the attached window belongs to another instance - isolation is broken");

            // Redirect proof (spec AC5): the app wrote settings into OUR directory.
            Retry.WhileFalse(
                () => Directory.Exists(_settingsDir) && Directory.EnumerateFiles(_settingsDir).Any(),
                TimeSpan.FromSeconds(5)).Result.ShouldBeTrue(
                "app did not write settings into the redirected directory");
        }

        /// <summary>Finds a top-level window of the app by exact title (dialogs included).</summary>
        public AutomationElement? GetTopLevelWindow(string title) =>
            Retry.WhileNull(() =>
                    App.GetAllTopLevelWindows(Automation)
                        .FirstOrDefault(w => w.Title == title),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)).Result;

        public void AssertAlive()
        {
            App.HasExited.ShouldBeFalse("the app exited unexpectedly");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { App.Close(); } catch { /* already gone */ }
            App.Kill();
            Automation.Dispose();

            foreach (var dir in new[] { _logDir }) // keep settings dir for post-run inspection on failure
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
```

Implementation notes: adjust namespace qualification style once (`using TaskManager.Infrastructure;` + unqualified `TaskManagerEnvironment` is cleaner than inline globals). If `Application.Launch(string, Action<ProcessStartInfo>)` differs in the pinned FlaUI version, use `Application.Launch(new ProcessStartInfo {...})` equivalent and report. Keep the settings dir until dispose-end intentionally (failure forensics); log dir deleted here AND uploaded by CI before teardown — CI ordering handled in Task 4 (upload happens between test step and job end; if impossible, move deletion behind an env flag `CI` check and report).

`tests/TaskManager.UiAutomationTests/UiCollection.cs`:

```csharp
namespace TaskManager.UiAutomationTests
{
    [CollectionDefinition("ui")]
    public sealed class UiCollection : ICollectionFixture<AppSession>
    {
    }
}
```

`tests/TaskManager.UiAutomationTests/Pages/MainWindowPage.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using TaskManager.Resources.Languages;

namespace TaskManager.UiAutomationTests.Pages
{
    /// <summary>Behavioral lookups on the main window. Names come from the app's own resources.</summary>
    public sealed class MainWindowPage
    {
        private readonly AutomationElement _window;

        public MainWindowPage(AutomationElement window) => _window = window;

        public string Title => _window.Title ?? string.Empty;

        public IReadOnlyList<string> StatusTexts()
        {
            var cf = _window.ConditionFactory?._;
```

Hmm — ConditionFactory access differs across FlaUI versions. SAFEST canonical form (use THIS):

```csharp
        public IReadOnlyList<string> StatusTexts()
        {
            var results = Retry.WhileEmpty(
                () => _window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                             .Select(e => e.Name)
                             .Where(n => !string.IsNullOrEmpty(n))
                             .ToArray(),
                TimeSpan.FromSeconds(5));
            return results.Result ?? [];
        }

        public AutomationElement? FindRowContaining(string text)
        {
            var found = Retry.WhileNull(
                () => _window.FindAllDescendants(cf => cf.ByControlType(ControlType.Row))
                             .FirstOrDefault(r => r.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true),
                TimeSpan.FromSeconds(12),
                TimeSpan.FromMilliseconds(250));
            return found.Result;
        }

        public void SelectRow(AutomationElement row) =>
            row.Patterns.SelectionItem.Pattern.Select();

        public void DeselectAllRows()
        {
            foreach (var row in _window.FindAllDescendants(cf => cf.ByControlType(ControlType.Row)))
            {
                var selection = row.Patterns.SelectionItem;
                if (selection.IsSupported && row.Patterns.SelectionItem.Pattern.IsSelected.Value)
                {
                    row.Patterns.SelectionItem.Pattern.Toggle();
                }
            }
        }

        /// <summary>Walks top-level menu names then leaf names, invoking the leaf (e.g. "Management", "TerminateProcesses").</summary>
        public void InvokeMenu(params string[] headers)
        {
            var current = _window;
            foreach (var header in headers)
            {
                var element = Retry.WhileNull(
                    () => current.FindFirstDescendant(cf => cf.ByName(header)),
                    TimeSpan.FromSeconds(5)).Result;
                element.ShouldNotBeNull($"menu element '{header}' not found");
                element.Patterns.Invoke.Pattern.Invoke();
                current = element;
            }
        }

        public void InvokeTopLevelMenuItem(string topLevelHeader, string leafHeader) =>
            InvokeMenu(topLevelHeader, leafHeader);
    }
}
```

DELETE the first broken `StatusTexts` draft — ship ONE implementation. The `cf` lambda parameter IS the ConditionFactory in FlaUI's `FindAll*` overloads (`FindAllDescendants(Func<ConditionFactory, ConditionBase<...>>)`); verify exact overload signature against the pinned FlaUI version and adapt (report if the lambda form doesn't exist — fallback: capture `var cf = _window.Automation.ConditionFactory;` explicitly and use `cf.ByControlType(...)` inside plain lambdas).

Menu invocation nuance: walking parent→child requires the parent EXPANDED before child search; if leaf-not-found occurs, insert `current.Patterns.ExpandCollapse.Pattern.Expand()` before searching children when the pattern is supported:

```csharp
                var expand = current.Patterns.ExpandCollapse;
                if (expand.IsSupported)
                {
                    expand.Pattern.Expand();
                }
```

Include this defensively inside `InvokeMenu`.

- [ ] **Step 3: The smoke test**

`tests/TaskManager.UiAutomationTests/SmokeLaunchTests.cs`:

```csharp
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using TaskManager.Resources.Languages;
using TaskManager.UiAutomationTests.Pages;

namespace TaskManager.UiAutomationTests
{
    [Collection("ui")]
    public class SmokeLaunchTests
    {
        private readonly AppSession _session;

        public SmokeLaunchTests(AppSession session) => _session = session;

        [Fact]
        public void Launch_ShowsLiveMainWindow()
        {
            _session.AssertAlive();

            var window = _session.App.GetMainWindow(_session.Automation, TimeSpan.FromSeconds(5));
            var page = new MainWindowPage(window);

            page.Title.ShouldBe(AppSession.MainWindowTitle);

            var statusTexts = page.StatusTexts();
            var countPattern = $"^{Regex.Escape(Strings.Processes)}: [1-9][0-9]*$";
            statusTexts.ShouldContain(t => Regex.IsMatch(t, countPattern),
                $"expected a '{Strings.Processes}: N' counter among [{string.Join(", ", statusTexts)}]");

            statusTexts.ShouldContain(string.Format(
                global::System.Globalization.CultureInfo.CurrentCulture,
                Strings.StatusEverySeconds, 10)); // Low frequency default
        }
    }
}
```

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj
dotnet test tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj
dotnet test tests/TaskManager.UnitTests
```

⚠️ GUI POLICY IN EFFECT: running the UIA test WILL briefly open the app window ON YOUR PC — this is the one sanctioned opt-in validation for verifying the harness itself. Announce it in your report ("opt-in harness smoke performed"). If you prefer not to, STOP after build and report DONE_WITH_CONCERNS leaving Step-3 test execution to CI.

Main suite must stay green: 169/169 ×2 (untouched).

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props tests/TaskManager.UiAutomationTests
git commit -m "feat: flaui harness with isolated app session and launch smoke"
```

---

### Task 3: Full behavioral inventory

**Files:**
- Create: `tests/TaskManager.UiAutomationTests/Pages/SettingsDialogPage.cs`
- Create: `tests/TaskManager.UiAutomationTests/Pages/AboutDialogPage.cs`
- Create: `tests/TaskManager.UiAutomationTests/VictimFactory.cs`
- Create: `tests/TaskManager.UiAutomationTests/BehaviorTests.cs`
- Modify: `tests/TaskManager.UiAutomationTests/Pages/MainWindowPage.cs` (small additions below)

**Interfaces:**
- Consumes: Task 2 harness (`AppSession`, `MainWindowPage`, `"ui"` collection).
- Produces: `VictimFactory` (spawn unique-named hidden victim; `string Name`, `int Id`, `bool HasExited`, `Kill()`); dialog pages; eight behavior tests H2–H7, S1, S2.

- [ ] **Step 1: VictimFactory**

`tests/TaskManager.UiAutomationTests/VictimFactory.cs`:

```csharp
using System.Diagnostics;
using System.IO;

namespace TaskManager.UiAutomationTests
{
    /// <summary>Spawns a uniquely named, invisible, safely killable cmd.exe copy.</summary>
    public sealed class VictimFactory : IDisposable
    {
        private readonly List<Process> _victims = [];

        public Victim Spawn()
        {
            var name = $"tmuitest-{Guid.NewGuid():N}.exe";
            var target = Path.Combine(Path.GetTempPath(), name);
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), target, overwrite: true);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = "/c ping -n 60 127.0.0.1 > nul",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            _victims.Add(process);
            return new Victim(process, name);
        }

        public void Dispose()
        {
            foreach (var victim in _victims)
            {
                try { if (!victim.HasExited) victim.Kill(); } catch { }
                victim.Dispose();
            }
        }
    }

    public sealed record Victim(Process Process, string DisplayName)
    {
        public int Id => Process.Id;
        public string RowSearchText => DisplayName; // grid row name contains the exe file name
        public bool HasExited => Process.HasExited;
        public void WaitUntilExited(TimeSpan timeout) =>
            Process.WaitForExit((int)timeout.TotalMilliseconds).ShouldBeTrue("victim refused to die");
    }
}
```

- [ ] **Step 2: Dialog pages**

`Pages/SettingsDialogPage.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace TaskManager.UiAutomationTests.Pages
{
    /// <summary>Automates the settings dialog: second ComboBox is refresh-frequency.</summary>
    public sealed class SettingsDialogPage
    {
        private readonly AutomationElement _dialog;

        public SettingsDialogPage(AutomationElement dialog) => _dialog = dialog;

        public void SelectLastRefreshFrequency()
        {
            var combos = _dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox));
            combos.Length.ShouldBeGreaterThanOrEqualTo(2);
            var frequency = combos[1];

            frequency.Patterns.ExpandCollapse.Pattern.Expand();
            var items = frequency.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            items.Length.ShouldBeGreaterThanOrEqualTo(4);
            items[^1].Patterns.SelectionItem.Pattern.Select(); // enum order ends with Paused
            frequency.Patterns.ExpandCollapse.Pattern.Collapse();
        }

        public void Save()
        {
            var save = _dialog.FindFirstDescendant(cf => cf.ByName(Resources.Strings.Save));
            save.ShouldNotBeNull();
            save.AsButton().Invoke();
        }
    }
}
```

`Pages/AboutDialogPage.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace TaskManager.UiAutomationTests.Pages
{
    public sealed class AboutDialogPage
    {
        private readonly AutomationElement _dialog;

        public AboutDialogPage(AutomationElement dialog) => _dialog = dialog;

        public string AllText =>
            string.Join("\n", _dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                                     .Select(t => t.Name));

        public void Ok()
        {
            var ok = _dialog.FindFirstDescendant(cf => cf.ByName(Resources.Strings.AboutOkButton));
            ok.ShouldNotBeNull();
            ok.AsButton().Invoke();
        }
    }
}
```

(`Resources.Strings` refers to `TaskManager.Resources.Languages.Strings` — add the using alias at file top.)

- [ ] **Step 3: MainWindowPage additions**

```csharp
        public void InvokeTerminateViaContextMenu(AutomationElement row)
        {
            row.ContextClick(); // right-click opens the row context menu (BetterDataGrid behavior)
            var item = Retry.WhileNull(
                () => _window.FindFirstDescendant(cf => cf.ByName(Resources.Strings.Terminate)),
                TimeSpan.FromSeconds(5)).Result;
            item.ShouldNotBeNull();
            item.Patterns.Invoke.Pattern.Invoke();
        }

        public AutomationElement? GetModalByTitle(string title) => throw new NotSupportedException("moved to AppSession.GetTopLevelWindow");
```

DROP that second stub — modals come from `_session.GetTopLevelWindow(title)`. If `ContextClick()` extension does not exist in the pinned FlaUI version, use raw input instead:

```csharp
            var clickable = row.GetClickablePoint();
            FlaUI.Core.Input.Mouse.MoveTo((int)clickable.X, (int)clickable.Y);
            FlaUI.Core.Input.Mouse.RightClick();
```

and report which you used. Verify the context menu actually lists localized `Strings.Terminate` (read `MainWindow.xaml` row ContextMenu — headers exist for Terminate/Copy/SetPriority/Monitor-era entries) and adapt the looked-up header if the shipped header differs.

- [ ] **Step 4: Behavior tests**

`tests/TaskManager.UiAutomationTests/BehaviorTests.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using TaskManager.Resources.Languages;
using TaskManager.UiAutomationTests.Pages;

namespace TaskManager.UiAutomationTests
{
    [Collection("ui")]
    public class BehaviorTests : IDisposable
    {
        private readonly AppSession _session;
        private readonly MainWindowPage _main;
        private readonly VictimFactory _victims = new();

        public BehaviorTests(AppSession session)
        {
            _session = session;
            _main = new MainWindowPage(_session.App.GetMainWindow(_session.Automation, TimeSpan.FromSeconds(5)));
        }

        private AutomationElement MainWindowElement =>
            _session.App.GetMainWindow(_session.Automation, TimeSpan.FromSeconds(5));

        private void DismissTopLevelByEnter(string title)
        {
            var box = _session.GetTopLevelWindow(title);
            box.ShouldNotBeNull($"expected dialog '{title}'");
            box.Focus();
            Keyboard.Press(FlaUI.Core.Input.VirtualKeyCodes.RETURN); // default button (OK)
        }

        private void CloseTopLevelWithEscape(string title)
        {
            var box = _session.GetTopLevelWindow(title);
            box.ShouldNotBeNull($"expected dialog '{title}'");
            box.Focus();
            Keyboard.Press(FlaUI.Core.Input.VirtualKeyCodes.ESCAPE);
        }

        [Fact]
        public void About_ShowsStampedVersionAndCommit()
        {
            _main.InvokeMenu(Strings.HelpMenu, Strings.AboutMenu);
            var dialog = _session.GetTopLevelWindow(Strings.AboutMenu);
            dialog.ShouldNotBeNull();
            var page = new AboutDialogPage(dialog);

            var stamped = FileVersionInfo.GetVersionInfo(_session.ExePath).ProductVersion!;
            var expectedVersion = stamped.Split('+')[0];
            var commitPart = stamped.Split('+').Skip(1).FirstOrDefault() ?? string.Empty;

            page.AllText.ShouldContain(expectedVersion);
            if (commitPart.Length > 0)
            {
                page.AllText.ShouldContain(commitPart[..Math.Min(7, commitPart.Length)]);
            }

            page.Ok();
            _session.AssertAlive();
        }

        [Fact]
        public void Settings_RoundTrip_PublishesPausedThenRestores()
        {
            _main.InvokeMenu(Strings.SettingsStr);
            var dialog = _session.GetTopLevelWindow("SettingsWindow");
            dialog.ShouldNotBeNull();

            var page = new SettingsDialogPage(dialog);
            page.SelectLastRefreshFrequency(); // last item = Paused
            page.Save();

            var main = MainWindowElement;
            Retry.WhileTrue(
                () => !new MainWindowPage(main).StatusTexts().Contains(Strings.StatusPaused),
                TimeSpan.FromSeconds(5)).Result.ShouldBeTrue("status strip did not show Paused");

            // restore Low so later tests see default cadence
            _main.InvokeMenu(Strings.SettingsStr);
            dialog = _session.GetTopLevelWindow("SettingsWindow");
            var restore = new SettingsDialogPage(dialog!);
            var combos = dialog!.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox));
            combos[1].Patterns.ExpandCollapse.Pattern.Expand();
            var items = combos[1].FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            items.Single(i => i.Name.Contains("Low")).Patterns.SelectionItem.Pattern.Select();
            combos[1].Patterns.ExpandCollapse.Pattern.Collapse();
            restore.Save();
        }

        [Fact]
        public void Export_OpensAndCancels()
        {
            _main.InvokeMenu(Strings.Export);
            var dialog = _session.GetTopLevelWindow("DataExportWindow");
            dialog.ShouldNotBeNull();

            CloseTopLevelWithEscape("DataExportWindow"); // no dedicated cancel button exists

            _session.AssertAlive();
        }

        [Fact]
        public void Terminate_Victim_HappyPath_KillsProcessAndRemovesRow()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();
            victim.HasExited.ShouldBeFalse();

            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull($"victim row '{victim.RowSearchText}' never appeared");
            _main.SelectRow(row!);

            _main.InvokeMenu(Strings.Management, Strings.TerminateProcesses);
            DismissTopLevelByEnter(Strings.Confirm); // confirmation -> OK

            victim.WaitUntilExited(TimeSpan.FromSeconds(5));
            _main.FindRowContaining(victim.RowSearchText).ShouldBeNull("row lingered past two refresh ticks");
        }

        [Fact]
        public void Terminate_CancelPath_VictimSurvives()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();
            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull();
            _main.SelectRow(row!);

            _main.InvokeMenu(Strings.Management, Strings.TerminateProcesses);
            CloseTopLevelWithEscape(Strings.Confirm);

            victim.HasExited.ShouldBeFalse();
        }

        [Fact]
        public void Priority_AppliesToVictimOnly()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();
            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull();
            _main.SelectRow(row!);

            _main.InvokeMenu(Strings.Management, Strings.SetPriority);
            var dialog = _session.GetTopLevelWindow("SetPriorityWindow");
            dialog.ShouldNotBeNull();

            var combo = dialog!.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox)).First();
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            var items = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            items[0].Patterns.SelectionItem.Pattern.Select(); // culture-neutral: any priority proves the flow
            combo.Patterns.ExpandCollapse.Pattern.Collapse();

            var confirm = dialog.FindFirstDescendant(cf => cf.ByName(Resources.Strings.Confirm));
            confirm!.AsButton().Invoke();

            victim.HasExited.ShouldBeFalse();
        }

        [Fact]
        public void Terminate_WithoutSelection_ShowsValidationError()
        {
            _main.DeselectAllRows();

            _main.InvokeMenu(Strings.Management, Strings.TerminateProcesses);

            DismissTopLevelByEnter(Strings.Error);
            _session.AssertAlive();
        }

        [Fact]
        public async Task SecondInstance_ExitsImmediately_AndOriginalStaysAlive()
        {
            var duplicate = Process.Start(new ProcessStartInfo
            {
                FileName = _session.ExePath, // inherits redirected env from our own process? NO -
                                             // set them explicitly below
            });
```

CORRECTION (apply this final form — env vars do NOT inherit from the test host, they must be re-set):

```csharp
        [Fact]
        public void SecondInstance_ExitsImmediately_AndOriginalStaysAlive()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _session.ExePath,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables[global::TaskManager.Infrastructure.TaskManagerEnvironment.InstanceName] =
                _session.InstanceName; // SAME instance name: the guard MUST bounce it

            using var duplicate = Process.Start(psi)!;
            duplicate.WaitForExit(5_000).ShouldBeTrue("second instance was not bounced");
            duplicate.ExitCode.ShouldBe(0);

            _session.AssertAlive();
            MainWindowElement.Title.ShouldBe(AppSession.MainWindowTitle);
        }
    }
}
```

Consequently `AppSession` must EXPOSE the generated instance name publicly (add `public string InstanceName { get; }` alongside the private local in Task 2's Step 2 — one-line change, fold into this task and note it).

Also delete the erroneous first `SecondInstance_…` draft (the one with the misleading comment); ship only the corrected final form.

- [ ] **Step 5: Run relevant validation**

```bash
dotnet build tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj
dotnet test tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj
dotnet test tests/TaskManager.UnitTests
```

UIA suite: 9/9 green (H1 + 8). GUI POLICY: executing the UIA suite locally opens windows — you MAY run it once to validate (announce "opt-in local UIA validation performed"), or leave full validation to CI and report that choice. Main suite: 169/169 untouched.

Known-flake triage: if exactly one test fails intermittently, run it filtered ×5 (`--filter-class`) before concluding; report flake rate + suspected cause rather than adding retries.

- [ ] **Step 6: Commit**

```bash
git add tests/TaskManager.UiAutomationTests
git commit -m "feat: behavioral flaui inventory over real app flows"
```

---

### Task 4: CI wiring + opt-in documentation

**Files:**
- Modify: `.github/workflows/ci.yml` (build + UIA steps, log artifact)
- Modify: `README.md` (Development notes: opt-in command)

**Interfaces:**
- Consumes: Task 2's project path; Task 1's log-dir default `artifacts/ui-test-logs` (relative to the test run's working directory = repo root on CI).
- Produces: CI executes UIA suite after unit tests; failed runs publish `artifacts/ui-test-logs`; README documents the opt-in command.

- [ ] **Step 1: Workflow**

In `.github/workflows/ci.yml`, AFTER the existing Build step and BEFORE the Unit-tests step, insert:

```yaml
      - name: Build UI automation project
        run: dotnet build tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj -c Release --no-restore --nologo
```

Modify the existing Unit-test step to remain unchanged, then ADD after it:

```yaml
      # Windows appear only on the ephemeral runner, never on developer machines.
      - name: UI automation tests (FlaUI)
        run: >
          dotnet test tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj
          -c Release --no-build

      - name: Upload UI-test logs on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: ui-test-logs
          path: artifacts/ui-test-logs/**/tm-*.log
          if-no-files-found: ignore
```

Keep the existing coverage-upload step last (its `if: always()` semantics unchanged). Note: the UIA step intentionally omits `--nologo` (MTP rejects it) and `--coverage` (not needed here).

- [ ] **Step 2: README**

In README "Development notes", append bullet:

```markdown
- Optional UI end-to-end suite (opens real windows — run deliberately):
  `dotnet test tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj`.
  Runs automatically in CI on GitHub's runner.
```

- [ ] **Step 3: Validation**

YAML sanity (skip gracefully if module missing):

```powershell
pwsh -NoProfile -Command "if (Get-Module -ListAvailable powershell-yaml) { ConvertFrom-Yaml (Get-Content .github/workflows/ci.yml -Raw) } else { 'yaml module unavailable - skipped' }"
```

Local mirror of what CI will run (THIS OPENS WINDOWS — you already opted in via this plan's Task 2 precedent; announce it):

```bash
dotnet build TaskManager.slnx -c Release --nologo
dotnet build tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj -c Release --nologo
dotnet test tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj -c Release --no-build
```

All green; `artifacts/ui-test-logs` may contain the run's log files (gitignored).

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml README.md
git commit -m "ci: flaui suite on runners with log artifacts"
```

Push the branch and watch the first full CI run — the UIA step's stability on the runner is the batch's final proof. Report the run URL/result if available.

---

## Self-Review Record

- **Spec coverage:** §2 project placement/packages → T2 S1; AppSession five duties incl. PID-ownership + redirect-proof (§2 items 1–3, AC5) → T2 S2; page objects + retry discipline (§2) → T2 S2/T3 S2–S3; VictimFactory (§2) → T3 S1; inventory H1–H7/S1–S2 (§3) → T2 S3 (H1) + T3 S4; production changes §4 items 1–4 → T1; failure story (§4) → publisher-style loud launch-fail (T2 S2), finally-dismissals (T3 tests), CI log artifact (T4 S1); §5 CI order + README opt-in + flake policy → T4; §7 AC1 structural exclusion (out-of-slnx, T2 S1), AC2 documented command (T4 S2), AC3 CI step (T4 S1), AC4 ten tests (T2+T3), AC5 redirect proof (T2 S2), AC6 CPM-confined packages (T2 S1), AC7 behavior-only assertions (inventory design). ✔
- **Placeholder scan:** every code block ships complete; FlaUI-version/overload uncertainties carry explicit verify-and-adapt instructions with concrete fallback forms; the two intentional draft-and-correction pairs (SecondInstance test, StatusTexts) are marked DELETE/SHIP explicitly so no executor keeps both. ✔
- **Type consistency:** `RefreshOutcome` unused here (correct — outcome asserted via strip TEXT); `AppSession.MainWindowTitle`/"Task Manager" consistent T2↔T3↔Global Constraints; `GetTopLevelWindow(string)` signature used identically in T3 helpers; `Victim.RowSearchText` matches `FindRowContaining(string)` usage; collection name `"ui"` matches `[Collection("ui")]`. ✔
