# Status Bar, About Dialog & Single Instance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Live status strip (count · interval/paused · refresh-outcome chip · elevation chip with relaunch action), an About dialog fed by stamped assembly metadata, and a single-instance guard with activate-existing-window semantics.

**Architecture:** The catalog (single writer of polling truth) publishes an immutable `RefreshDiagnostics` through the same INPC/dispatcher discipline as `ProcessCount`; dialogs flow through `IWindowService`; a named-mutex `SingleInstanceGuard` gates startup, with a `--await-instance` argument giving restart/relaunch successors a bounded handoff window so plain launches never wait.

**Tech Stack:** WPF/.NET 10, CommunityToolkit.Mvvm, BCL `System.Threading.Channels`-free (mutex + `[LibraryImport]` interop), xunit.v3/MTP + NSubstitute + FakeTimeProvider. Zero new packages.

**Spec:** `docs/superpowers/specs/2026-08-25-status-bar-about-single-instance-design.md` (authoritative).

## Global Constraints

- Branch `build/engineering-guardrails` (verify with `git branch --show-current`; never switch/push). Suite baseline: **146 tests green**, zero warnings (`TreatWarningsAsErrors`).
- Zero package additions; Central Package Management rules apply.
- All user-visible strings go through `Resources/Languages/Strings.resx` + `Strings.pl.resx` AND their hand-maintained `Strings.Designer.cs` (build does NOT regenerate the designer — every new key needs a matching property added there, following the existing member pattern).
- Localization access from C# follows the existing `Strings.Xxx` static pattern; XAML uses `{x:Static resx:Strings.Xxx}`.
- P/Invoke uses the `[LibraryImport]` source generator (partial class members), not `[DllImport]`.
- Commit messages: lowercase conventional prefixes (`feat:`, `fix:`, `test:`, `docs:`).
- Full-suite validation command: `dotnet test tests/TaskManager.UnitTests` (MTP runner — `--nologo` is rejected by test invocations; class filtering via `--filter-class <FullyQualifiedName>`).
- Windows-only; solution file `TaskManager.slnx`.

---

### Task 1: Catalog refresh diagnostics

**Files:**
- Create: `src/TaskManager.Presentation/../Presentation` — NOTE: correct path is `src/TaskManager/Presentation/RefreshDiagnostics.cs`
- Modify: `src/TaskManager/Presentation/ProcessListCatalog.cs`
- Test: `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogDiagnosticsTests.cs` (new)

**Interfaces:**
- Consumes: existing `_timeProvider`, `_dispatcher`, `_logger`, gate/skip/fail paths in `ProcessListCatalog`; existing test helpers (`ScriptedEnumerator`, `CountingEnricher`, `InlineDispatcher` pattern, `ProcessFakes.Snap`, `SettingsChangedTo` pattern from `ProcessListCatalogPollingTests.cs`).
- Produces (consumed by Task 2's view model):
  - `namespace TaskManager.Presentation`: `public enum RefreshOutcome { Ok, Skipped, Failed }` and `public sealed record RefreshDiagnostics(double LastDurationMs, RefreshOutcome Outcome, DateTimeOffset CompletedAt)`
  - `public RefreshDiagnostics? LastRefresh { get; private set; }` — INPC, null until first attempt completes
  - `public bool IsPollingPaused { get; private set; }` — INPC, true until polling configured otherwise

- [ ] **Step 1: Create the diagnostics types**

`src/TaskManager/Presentation/RefreshDiagnostics.cs`:

```csharp
namespace TaskManager.Presentation
{
    public enum RefreshOutcome
    {
        Ok,
        Skipped,
        Failed,
    }

    /// <summary>Outcome snapshot of the most recent refresh attempt (any of the three outcomes).</summary>
    public sealed record RefreshDiagnostics(
        double LastDurationMs,
        RefreshOutcome Outcome,
        DateTimeOffset CompletedAt);
}
```

- [ ] **Step 2: Wire tracking into `ProcessListCatalog`**

Add fields + properties near `ProcessCount` (reuse its `OnPropertyChanged([CallerMemberName])` helper):

```csharp
        private double _lastCompletedDurationMs;

        private RefreshDiagnostics? _lastRefresh;
        public RefreshDiagnostics? LastRefresh
        {
            get => _lastRefresh;
            private set
            {
                if (!Equals(_lastRefresh, value))
                {
                    _lastRefresh = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPollingPaused = true; // nothing flows until polling is configured
        public bool IsPollingPaused
        {
            get => _isPollingPaused;
            private set
            {
                if (_isPollingPaused != value)
                {
                    _isPollingPaused = value;
                    OnPropertyChanged();
                }
            }
        }
```

All mutations happen on the UI thread via the existing dispatcher — add this publisher:

```csharp
        private void PublishRefresh(double durationMs, RefreshOutcome outcome)
        {
            _dispatcher.Invoke(() =>
            {
                if (outcome == RefreshOutcome.Ok)
                {
                    _lastCompletedDurationMs = durationMs;
                }

                LastRefresh = new RefreshDiagnostics(durationMs, outcome, _timeProvider.GetLocalNow());
            });
        }
```

Hook points (read the current method bodies first — they contain the L2 trace and the drain-era comments):

1. `InitializeAsync()` — after `StartPolling()`, add: `IsPollingPaused = CurrentIntervalSeconds == 0;`
2. `OnSettingsChanged(...)` — after the existing logging/restart lines, add: `IsPollingPaused = seconds == 0;`
3. `SafePollingRefreshAsync()` skip branch — inside the existing `if (!_refreshGate.Wait(0))` block, before `return;`, add:
   ```csharp
                PublishRefresh(_lastCompletedDurationMs, RefreshOutcome.Skipped);
   ```
4. `SafePollingRefreshAsync()` catch block — alongside the existing `LogWarning`, add:
   ```cSharp
                PublishRefresh(_lastCompletedDurationMs, RefreshOutcome.Failed);
   ```
5. `RefreshCoreAsync()` — after `ApplyBatch(batch.Batch, batch.Enrichments);` (the LAST statement), add:
   ```csharp
            var totalElapsed = _timeProvider.GetElapsedTime(startTimestamp);
            PublishRefresh(totalElapsed.TotalMilliseconds, RefreshOutcome.Ok);
   ```
   (`startTimestamp` already exists as the first statement from the L2 trace work; this duration deliberately INCLUDES `ApplyBatch` — it is the full operation cost, unlike the trace which excludes UI marshaling.)

- [ ] **Step 3: Add the tests**

Create `tests/TaskManager.UnitTests/Presentation/ProcessListCatalogDiagnosticsTests.cs` (mirror the using-block and helper approach of `ProcessListCatalogPollingTests.cs`; if `InlineDispatcher` is file-local there, copy the smallest equivalent stub here):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Presentation;
using TaskManager.UnitTests.TestSupport;

namespace TaskManager.UnitTests.Presentation
{
    public class ProcessListCatalogDiagnosticsTests
    {
        private readonly ScriptedEnumerator _enumerator = new();
        private readonly IProcessOperations _ops = Substitute.For<IProcessOperations>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly FakeTimeProvider _time = new();

        private ProcessListCatalog CreateCatalog(RefreshFrequencyType frequency = RefreshFrequencyType.Low)
        {
            _settings.Current.Returns(AppSettings.Defaults with { ProcessesRefreshFrequency = frequency });
            return new ProcessListCatalog(
                InlineDispatcher, _enumerator, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);
        }

        private void SettingsChangedTo(RefreshFrequencyType frequency)
        {
            var settings = AppSettings.Defaults with { ProcessesRefreshFrequency = frequency };
            _settings.Current.Returns(settings);
            _settings.Changed += Raise.Event<Action<AppSettings>>(settings);
        }

        [Fact]
        public async Task SuccessfulFill_PublishesOkDiagnostics()
        {
            var catalog = CreateCatalog();
            _enumerator.Queue(ProcessFakes.Snap(1));

            var raises = 0;
            catalog.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProcessListCatalog.LastRefresh)) raises++; };

            await catalog.LoadForTestAsync();

            catalog.LastRefresh.ShouldNotBeNull();
            catalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Ok);
            catalog.LastRefresh.LastDurationMs.ShouldBeGreaterThanOrEqualTo(0);
            raises.ShouldBe(1);
        }

        [Fact]
        public async Task GateBusy_SecondAttempt_PublishesSkipped_ThenFirstCompletesOk()
        {
            var catalog = CreateCatalog();
            var gateOpened = new TaskCompletionSource();
            var blockingEnumerator = new GatedEnumerator(gateOpened.Task);

            // occupy the gate: start a refresh whose Capture blocks until released
            var first = catalog.LoadForTestAsync();

            // second attempt hits the busy gate and must publish Skipped
            await catalog.LoadForTestAsync();

            gateOpened.SetResult();
            await first;

            catalog.LastRefresh.ShouldNotBeNull();
            catalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Ok); // first (successful) attempt wins last-write
        }

        [Fact]
        public async Task EnumeratorThrows_PublishesFailed_AndDoesNotThrow()
        {
            var catalog = CreateCatalog();
            var throwing = new ThrowingEnumerator();
            var failingCatalog = new ProcessListCatalog(
                InlineDispatcher, throwing, new CountingEnricher(), _settings, _ops,
                _time, NullLogger<ProcessListCatalog>.Instance);

            await failingCatalog.LoadForTestAsync(); // must not propagate

            failingCatalog.LastRefresh.ShouldNotBeNull();
            failingCatalog.LastRefresh.Outcome.ShouldBe(RefreshOutcome.Failed);
        }

        [Fact]
        public async Task PausedSetting_FlipsIsPollingPaused()
        {
            var catalog = CreateCatalog(RefreshFrequencyType.High);
            _enumerator.Queue(ProcessFakes.Snap(1));
            await catalog.LoadForTestAsync();

            SettingsChangedTo(RefreshFrequencyType.Paused);
            catalog.IsPollingPaused.ShouldBeTrue();

            SettingsChangedTo(RefreshFrequencyType.High);
            catalog.IsPollingPaused.ShouldBeFalse();
        }

        private sealed class GatedEnumerator(Task gate) : ISystemProcessEnumerator
        {
            public IReadOnlyList<ProcessSnapshot> Capture()
            {
                gate.Wait(TimeSpan.FromSeconds(5));
                return [ProcessFakes.Snap(99)];
            }
        }

        private sealed class ThrowingEnumerator : ISystemProcessEnumerator
        {
            public IReadOnlyList<ProcessSnapshot> Capture() =>
                throw new InvalidOperationException("boom");
        }
    }
}
```

If `ISystemProcessEnumerator.Capture`'s exact signature differs, adapt the two stubs to the real signature and report it. If the Skipped test proves impossible with the real gate semantics (e.g., `LoadForTestAsync` serializes differently), substitute a direct-call variant and report what you found.

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green twice (baseline 146 → ≈150).

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager/Presentation/RefreshDiagnostics.cs src/TaskManager/Presentation/ProcessListCatalog.cs tests/TaskManager.UnitTests/Presentation/ProcessListCatalogDiagnosticsTests.cs
git commit -m "feat: catalog publishes refresh diagnostics and paused state"
```

---

### Task 2: Elevation service + status bar strip

**Files:**
- Create: `src/TaskManager/Abstractions/IElevationService.cs`
- Create: `src/TaskManager/Services/ElevationService.cs`
- Create: `src/TaskManager/UI/Converters/RefreshOutcomeToBrushConverter.cs`
- Modify: `src/TaskManager/Infrastructure/Composition/UiServicesRegistration.cs` (one registration)
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `src/TaskManager/UI/Views/MainWindow.xaml` (replace Row 3)
- Modify: `src/TaskManager/Resources/Languages/Strings.resx`, `Strings.pl.resx`, `Strings.Designer.cs` (new keys)
- Test: `tests/TaskManager.UnitTests/UI/Converters/RefreshOutcomeToBrushConverterTests.cs` (new)

**Interfaces:**
- Consumes: Task 1's `LastRefresh`/`IsPollingPaused` (INPC on `IProcessListCatalog` implementations); existing `Strings` resource machinery; existing `_errorHandler.Guard*` extensions.
- Produces:
  - `public interface IElevationService { bool IsAdministrator { get; } }` + DI singleton
  - `MainWindowViewModel` ctor gains `IElevationService elevationService` parameter (DI auto-resolves; fix any manual constructions)
  - New VM members consumed by XAML: `IntervalText` (string), `OutcomeText` (string), `OutcomeKind` (`RefreshOutcome?`), `ElevationText` (string), `CanRelaunchElevated` (bool), `RelaunchElevatedCommand` (ICommand)
  - `RefreshOutcomeToBrushConverter` (Ok → `#FF6CC24A`, Skipped → `#FF9E9E9E`, Failed → `#FFD13438`, null → `#FF9E9E9E`)

- [ ] **Step 1: Elevation abstraction + implementation**

`src/TaskManager/Abstractions/IElevationService.cs`:

```csharp
namespace TaskManager.Abstractions
{
    /// <summary>Elevation of the current process, evaluated once.</summary>
    public interface IElevationService
    {
        bool IsAdministrator { get; }
    }
}
```

`src/TaskManager/Services/ElevationService.cs`:

```csharp
using System.Security.Principal;
using TaskManager.Abstractions;

namespace TaskManager.Services
{
    internal sealed class ElevationService : IElevationService
    {
        public bool IsAdministrator { get; } =
            new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

Register in `UiServicesRegistration.cs` alongside the other singletons (match file style):

```csharp
            services.AddSingleton<IElevationService, ElevationService>();
```

(+ needed `using TaskManager.Services;` / `using TaskManager.Abstractions;` if absent.)

- [ ] **Step 2: Resource strings**

Add to `Strings.resx` (EN) / `Strings.pl.resx` (PL) AND matching properties in `Strings.Designer.cs` (copy the existing property pattern exactly):

| Key | EN | PL |
|---|---|---|
| `StatusEverySeconds` | `every {0} s` | `co {0} s` |
| `StatusPaused` | `Paused` | `Wstrzymane` |
| `StatusOutcomeOk` | `Last refresh completed` | `Ostatnie odświeżenie zakończone` |
| `StatusOutcomeSkipped` | `Previous refresh still running` | `Poprzednie odświeżanie w toku` |
| `StatusOutcomeFailed` | `Last refresh failed` | `Ostatnie odświeżenie nieudane` |
| `StatusAdministrator` | `Administrator` | `Administrator` |
| `StatusStandard` | `Standard user` | `Zwykły użytkownik` |
| `StatusRelaunchAsAdmin` | `Relaunch as administrator` | `Uruchom ponownie jako administrator` |
| `HelpMenu` | `Help` | `Pomoc` |
| `AboutMenu` | `About` | `O programie` |
| `AboutVersionLabel` | `Version` | `Wersja` |
| `AboutCommitLabel` | `Commit` | `Commit` |
| `AboutLicenseLabel` | `License: see LICENSE.txt` | `Licencja: zobacz LICENSE.txt` |
| `AboutOkButton` | `OK` | `OK` |

(About keys land in Task 3 but adding them now avoids touching the designer twice.) Designer property template:

```csharp
        public static string StatusPaused {
            get {
                return ResourceManager.GetString("StatusPaused", resourceCulture);
            }
        }
```

- [ ] **Step 3: View-model wiring**

In `MainWindowViewModel`:

1. Ctor gains `IElevationService elevationService` (store field `_elevation`). Check for manual `new MainWindowViewModel(...)` constructions anywhere (grep src/ + tests/) and update them.
2. Extend the existing `_catalog.PropertyChanged` subscription to also forward `nameof(IProcessListCatalog.LastRefresh)` — check `IProcessListCatalog` declares the new members (add to the interface + catalog implements implicitly; they're already public on the class).

   IMPORTANT: if `IProcessListCatalog` does not expose them yet, add to the interface:
   ```csharp
   RefreshDiagnostics? LastRefresh { get; }
   bool IsPollingPaused { get; }
   ```
   (with `using TaskManager.Presentation;` on the interface file ONLY if layering allows — `IProcessListCatalog` lives in `TaskManager.Abstractions` (app layer) which may reference Presentation types freely.)
3. Subscribe `_settings.Changed` for interval text refresh (pattern identical to catalog's `OnSettingsChanged` subscription) and compute derived strings:

```csharp
        public string IntervalText
        {
            get
            {
                var seconds = RefreshFrequencies.SecondsMapping[_settings.Current.ProcessesRefreshFrequency];
                return seconds == 0
                    ? Strings.StatusPaused
                    : string.Format(CultureInfo.CurrentCulture, Strings.StatusEverySeconds, seconds);
            }
        }

        public string OutcomeText => _catalog.LastRefresh?.Outcome switch
        {
            RefreshOutcome.Ok => Strings.StatusOutcomeOk,
            RefreshOutcome.Skipped => Strings.StatusOutcomeSkipped,
            RefreshOutcome.Failed => Strings.StatusOutcomeFailed,
            _ => "-",
        };

        public RefreshOutcome? OutcomeKind => _catalog.LastRefresh?.Outcome;

        public string ElevationText => _elevation.IsAdministrator
            ? Strings.StatusAdministrator
            : Strings.StatusStandard;

        public bool CanRelaunchElevated => !_elevation.IsAdministrator;

        public ICommand RelaunchElevatedCommand { get; }
```

Raise `PropertyChanged` for these five members inside the catalog-subscription handler (for `IntervalText`, also in the settings.Changed handler). Add `RelaunchElevatedCommand = new RelayCommand(() => _errorHandler.Guard(App.RelaunchElevated, "relaunching as administrator"));` in the ctor — if only `GuardAsync` exists on the error-handler extensions, wrap: `new RelayCommand(() => _errorHandler.GuardAsync(() => { App.RelaunchElevated(); return Task.CompletedTask; }, "relaunching as administrator"));` and report which form you used. `App.RelaunchElevated()` lands in Task 4 — for NOW reference it and accept the compile break is NOT acceptable, so instead: create the command targeting a private method `RelaunchElevated()` containing `throw new NotImplementedException();`… **No.** Better ordering: introduce the real static in this task as a stub-free minimal implementation placed in `App`:

```csharp
        /// <summary>Relaunches the app requesting elevation; UAC decline is a silent no-op.</summary>
        internal static void RelaunchElevated()
        {
            // full implementation (guard handoff) arrives with the single-instance task;
            // this provisional body keeps the command functional without a mutex present.
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            System.Diagnostics.Process.Start(psi);
            Application.Current.Shutdown();
        }
```

Task 4 replaces this body with the guard-aware version and adds `Arguments = "--await-instance"` + cancel handling. Report this staging in your commit body.

4. Needed usings: `System.Globalization`, `TaskManager.Presentation` (OutcomeKind), plus existing ones.

- [ ] **Step 4: Converter**

`src/TaskManager/UI/Converters/RefreshOutcomeToBrushConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TaskManager.Presentation;

namespace TaskManager.UI.Converters
{
    /// <summary>Maps a refresh outcome to its status-dot brush; null (no sample yet) maps to neutral gray.</summary>
    public sealed class RefreshOutcomeToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                RefreshOutcome.Ok => Brushes.ForestGreen,
                RefreshOutcome.Failed => Brushes.IndianRed,
                _ => Brushes.Gray, // Skipped or no sample yet
            };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 5: Status strip XAML**

In `MainWindow.xaml` declare the converter resource in `<Window.Resources>`:

```xml
        <converters:RefreshOutcomeToBrushConverter x:Key="OutcomeToBrush"/>
```

(add `xmlns:converters="clr-namespace:TaskManager.UI.Converters"` next to the other xmlns entries if missing.)

Replace the Row-3 `TextBlock` (the MultiBinding `Processes: {ProcessCount}` block, roughly lines 122-129) with:

```xml
        <DockPanel Grid.Row="3" Margin="5,0,5,0" LastChildFill="False">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock>
                    <TextBlock.Text>
                        <MultiBinding StringFormat="{}{0}: {1}">
                            <Binding Source="{x:Static resx:Strings.Processes}" />
                            <Binding Path="ProcessCount" />
                        </MultiBinding>
                    </TextBlock.Text>
                </TextBlock>
                <TextBlock Margin="12,0,0,0" Text="{Binding IntervalText}"/>
                <Ellipse Width="8" Height="8" Margin="12,0,0,0"
                         VerticalAlignment="Center"
                         Fill="{Binding OutcomeKind, Converter={StaticResource OutcomeToBrush}}"
                         ToolTip="{Binding OutcomeText}"/>
            </StackPanel>
            <Button DockPanel.Dock="Right" HorizontalAlignment="Right"
                    Visibility="{Binding CanRelaunchElevated, Converter={StaticResource BoolToVisibility}}"
                    Command="{Binding RelaunchElevatedCommand}"
                    ToolTip="{x:Static resx:Strings.StatusRelaunchAsAdmin}"
                    Content="{x:Static resx:Strings.StatusStandard}"/>
            <TextBlock DockPanel.Dock="Right" HorizontalAlignment="Right" Margin="0,0,12,0"
                       Text="{Binding ElevationText}"
                       Visibility="{Binding IsAdministratorBadgeVisible, ...}"/>
        </DockPanel>
```

SIMPLIFY per this ruling instead of the ambiguous double-element tail above: render EXACTLY ONE elevation element via a style trigger —

```xml
            <ContentControl DockPanel.Dock="Right" HorizontalAlignment="Right">
                <ContentControl.Style>
                    <Style TargetType="ContentControl">
                        <Setter Property="Content">
                            <Setter.Value>
                                <TextBlock Text="{Binding ElevationText}"/>
                            </Setter.Value>
                        </Setter>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding CanRelaunchElevated}" Value="True">
                                <Setter Property="Content">
                                    <Setter.Value>
                                        <Button Command="{Binding RelaunchElevatedCommand}"
                                                ToolTip="{x:Static resx:Strings.StatusRelaunchAsAdmin}"
                                                Content="{x:Static resx:Strings.StatusStandard}"/>
                                    </Setter.Value>
                                </Setter>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </ContentControl.Style>
            </ContentControl>
```

Use the ContentControl version (drop the two-element tail sketch). If `BoolToVisibility` converter doesn't exist as a resource yet, either register the framework `BooleanToVisibilityConverter` as `<BooleanToVisibilityConverter x:Key="BoolToVisibility"/>` in resources or drop the Visibility attribute on the button (the ContentControl trigger already swaps elements) — prefer dropping it and say so.

- [ ] **Step 6: Converter test**

`tests/TaskManager.UnitTests/UI/Converters/RefreshOutcomeToBrushConverterTests.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using TaskManager.Presentation;
using TaskManager.UI.Converters;

namespace TaskManager.UnitTests.UI
{
    public class RefreshOutcomeToBrushConverterTests
    {
        private readonly RefreshOutcomeToBrushConverter _converter = new();

        [Theory]
        [InlineData(RefreshOutcome.Ok, nameof(Brushes.ForestGreen))]
        [InlineData(RefreshOutcome.Failed, nameof(Brushes.IndianRed))]
        [InlineData(RefreshOutcome.Skipped, nameof(Brushes.Gray))]
        public void Convert_MapsOutcomeToExpectedBrush(RefreshOutcome outcome, string expectedBrushName)
        {
            var expected = (SolidColorBrush)typeof(Brushes)
                .GetProperty(expectedBrushName)!
                .GetValue(null)!;

            var brush = _converter.Convert(outcome, typeof(Brush), null, CultureInfo.InvariantCulture);

            brush.ShouldBeOfType<SolidColorBrush>();
            ((SolidColorBrush)brush).Color.ShouldBe(expected.Color);
        }

        [Fact]
        public void Convert_NullMapsToNeutralGray()
        {
            var brush = _converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);
            ((SolidColorBrush)brush).Color.ShouldBe(((SolidColorBrush)Brushes.Gray).Color);
        }

        [Fact]
        public void ConvertBack_IsNotSupported()
        {
            Should.Throw<NotSupportedException>(() =>
                _converter.ConvertBack(null, typeof(object), null, CultureInfo.InvariantCulture));
        }
    }
}
```

(Match the namespace used by existing files under `tests/TaskManager.UnitTests/UI/Converters/` if any exist; adjust STA needs — brush creation may require STA; the repo references `Xunit.StaFact` — if `Brushes` access throws for non-STA, switch the theory to `[StaTheory]` from `Xunit.StaFact` and report it.)

- [ ] **Step 7: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green (≈151). Then launch the app once (`dotnet run --project src/TaskManager`) and confirm visually/by screenshot-less inspection that the strip renders: count, "every N s", dot, and the elevation element (button when running non-admin). Close normally. Report what you saw; use the programmatic close fallback if needed.

- [ ] **Step 8: Commit**

```bash
git add -A src/TaskManager tests/TaskManager.UnitTests
git commit -m "feat: live status strip with refresh outcome chip and elevation action"
```

---

### Task 3: About dialog

**Files:**
- Create: `src/TaskManager/Services/AboutInfo.cs`
- Create: `src/TaskManager/UI/Views/AboutWindow.xaml` + `.xaml.cs`
- Modify: `src/TaskManager/Abstractions/IWindowService.cs`, `src/TaskManager/Services/WindowService.cs`
- Modify: `src/TaskManager/ViewModels/MainWindowViewModel.cs` (command), `src/TaskManager/UI/Views/MainWindow.xaml` (Help menu)
- Test: `tests/TaskManager.UnitTests/Services/AboutInfoTests.cs` (new)

**Interfaces:**
- Consumes: Task 2's resx keys (`HelpMenu`, `AboutMenu`, `AboutVersionLabel`, `AboutCommitLabel`, `AboutLicenseLabel`, `AboutOkButton`); `InformationalVersion` stamped as `<version>+<sha>` by Wave 1 CI (local builds: `0.1.0` prefix, possibly without `'+'`).
- Produces: `IWindowService.ShowAbout()`; `internal static class AboutInfo` with `Version`, `Commit`, `Parse(string informationalVersion)` returning `(string Version, string Commit)`.

- [ ] **Step 1: AboutInfo helper**

`src/TaskManager/Services/AboutInfo.cs`:

```csharp
using System.Reflection;

namespace TaskManager.Services
{
    /// <summary>Parses the stamped InformationalVersion (&lt;version&gt;+&lt;sha&gt;) for the About dialog.</summary>
    internal static class AboutInfo
    {
        public static string Version { get; } = Parse(InformationalVersion()).Version;
        public static string Commit { get; } = Parse(InformationalVersion()).Commit;

        internal static (string Version, string Commit) Parse(string informationalVersion)
        {
            var separatorIndex = informationalVersion.IndexOf('+');
            return separatorIndex < 0
                ? (informationalVersion, string.Empty)
                : (informationalVersion[..separatorIndex], informationalVersion[(separatorIndex + 1)..]);
        }

        private static string InformationalVersion() =>
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? "0.0.0";
    }
}
```

- [ ] **Step 2: Parse tests**

`tests/TaskManager.UnitTests/Services/AboutInfoTests.cs` (match folder namespace conventions):

```csharp
using TaskManager.Services;

namespace TaskManager.UnitTests.Services
{
    public class AboutInfoTests
    {
        [Theory]
        [InlineData("1.2.3+1a2b3c", "1.2.3", "1a2b3c")]
        [InlineData("0.1.0", "0.1.0", "")]
        [InlineData("", "", "")]
        [InlineData("1.2.3+a+b", "1.2.3", "a+b")]
        public void Parse_SplitsOnFirstPlus(string input, string expectedVersion, string expectedCommit)
        {
            var (version, commit) = AboutInfo.Parse(input);

            version.ShouldBe(expectedVersion);
            commit.ShouldBe(expectedCommit);
        }
    }
}
```

- [ ] **Step 3: The window**

`src/TaskManager/UI/Views/AboutWindow.xaml`:

```xml
<Window x:Class="TaskManager.UI.Views.AboutWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:resx="clr-namespace:TaskManager.Resources.Languages"
        Title="{x:Static resx:Strings.AboutMenu}"
        Width="420" Height="240"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False">
    <StackPanel Margin="20">
        <TextBlock FontSize="20" FontWeight="Bold" Text="Task Manager"/>
        <TextBlock Margin="0,10,0,0">
            <Run Text="{x:Static resx:Strings.AboutVersionLabel}"/>
            <Run Text=" "/><Run x:Name="VersionRun" FontWeight="SemiBold"/>
        </TextBlock>
        <TextBlock>
            <Run Text="{x:Static resx:Strings.AboutCommitLabel}"/>
            <Run Text=" "/><Run x:Name="CommitRun" FontWeight="SemiBold"/>
        </TextBlock>
        <TextBlock Text="{x:Static resx:Strings.AboutLicenseLabel}"/>
        <TextBlock Margin="0,10,0,0">
            <Hyperlink RequestNavigate="OnOpenRepository">
                <Run Text="https://github.com/lukegor/TaskManager"/>
            </Hyperlink>
        </TextBlock>
        <Button Content="{x:Static resx:Strings.AboutOkButton}"
                Width="80" Height="24" Margin="0,16,0,0"
                HorizontalAlignment="Right" IsDefault="True" Click="OnClose"/>
    </StackPanel>
</Window>
```

`src/TaskManager/UI/Views/AboutWindow.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows.Navigation;
using TaskManager.Services;

namespace TaskManager.UI.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionRun.Text = AboutInfo.Version;
            CommitRun.Text = string.IsNullOrEmpty(AboutInfo.Commit)
                ? "-"
                : AboutInfo.Commit;
        }

        private void OnOpenRepository(object sender, RequestNavigateEventArgs e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
```

Check the csproj includes new `.xaml` pages automatically (SDK globbing handles Page items for WPF projects — verify `bin` output builds; if the project uses explicit Page items, add one mirroring SettingsWindow).

- [ ] **Step 4: Service + menu wiring**

`IWindowService.cs` — add:

```csharp
        /// <summary>Opens the about dialog modally.</summary>
        void ShowAbout();
```

`WindowService.cs` — add method + factory (mirroring `ShowSettings`/`CreateSettingsDialog`):

```csharp
    public void ShowAbout() => CreateAboutDialog().ShowDialog();

    internal AboutWindow CreateAboutDialog() => new();
```

`MainWindowViewModel` — add command next to `OpenSettingsCommand`:

```csharp
        public ICommand OpenAboutCommand { get; }
// in ctor:
        OpenAboutCommand = new RelayCommand(() => _windows.ShowAbout());
```

`MainWindow.xaml` menu — after the Export item, before Settings:

```xml
            <MenuItem Header="{x:Static resx:Strings.HelpMenu}">
                <MenuItem Header="{x:Static resx:Strings.AboutMenu}" Command="{Binding OpenAboutCommand}"/>
            </MenuItem>
```

- [ ] **Step 5: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green (≈146 + previous growth + 4 parse cases).

Manual: launch app → Help → About → verify version matches `(Get-Item bin\Debug\TaskManager.dll).VersionInfo.InformationalVersion` split, link opens browser, OK closes.

- [ ] **Step 6: Commit**

```bash
git add src/TaskManager tests/TaskManager.UnitTests
git commit -m "feat: about dialog with stamped version and repository link"
```

---

### Task 4: Single-instance guard + app wiring

**Files:**
- Create: `src/TaskManager/Infrastructure/SingleInstanceGuard.cs`
- Modify: `src/TaskManager/App.xaml.cs`
- Test: `tests/TaskManager.UnitTests/Infrastructure/SingleInstanceGuardTests.cs` (new)

**Interfaces:**
- Consumes: Task 2's provisional `App.RelaunchElevated()` body (replaced here).
- Produces: `internal sealed partial class SingleInstanceGuard(bool waitForExistingRelease)` with `bool IsFirstInstance { get; }`, `void ActivateFirstInstanceWindow()`, `IDisposable` disposal releasing the mutex; `App.RelaunchElevated()` final behavior; `App.Restart()` passes `--await-instance`.

- [ ] **Step 1: Implement the guard**

`src/TaskManager/Infrastructure/SingleInstanceGuard.cs`:

```csharp
using System.Diagnostics;

namespace TaskManager.Infrastructure
{
    /// <summary>
    /// Per-session single instancing via a named mutex. Normal mode decides instantly
    /// (createdNew); handoff mode (restart/relaunch successors carrying --await-instance)
    /// waits up to 10 s for the predecessor to release. Activation brings the first
    /// instance's window to the foreground.
    /// </summary>
    internal sealed partial class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = "Local\\TaskManager.SingleInstance";
        private const int RestoreCommand = 9; // SW_RESTORE

        private Mutex? _mutex;

        public SingleInstanceGuard(bool waitForExistingRelease)
        {
            if (!waitForExistingRelease)
            {
                _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
                IsFirstInstance = createdNew;
                if (!IsFirstInstance)
                {
                    _mutex.Dispose();
                    _mutex = null;
                }

                return;
            }

            // Handoff: attach to the existing mutex and wait for the predecessor to release.
            _ = Mutex.TryOpenExisting(MutexName, out var existing);
            if (existing is null)
            {
                // predecessor exited before we attached: we are the successor owner.
                _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
                IsFirstInstance = createdNew;
                if (!IsFirstInstance)
                {
                    _mutex.Dispose();
                    _mutex = null;
                }

                return;
            }

            try
            {
                IsFirstInstance = existing.WaitOne(TimeSpan.FromSeconds(10));
                if (IsFirstInstance)
                {
                    _mutex = existing;
                }
                else
                {
                    existing.Dispose();
                }
            }
            catch (AbandonedMutexException)
            {
                // predecessor crashed while holding: ownership transferred to us.
                IsFirstInstance = true;
                _mutex = existing;
            }
        }

        public bool IsFirstInstance { get; private set; }

        public void ActivateFirstInstanceWindow()
        {
            var currentId = Environment.ProcessId;
            var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);

            foreach (var process in Process.GetProcessesByName(name))
            {
                using var _ = process;
                if (process.Id == currentId || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (IsIconic(process.MainWindowHandle))
                {
                    ShowWindow(process.MainWindowHandle, RestoreCommand);
                }

                _ = SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }

        public void Dispose()
        {
            _mutex?.Dispose();
            _mutex = null;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsIconic(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
```

Required usings: `System.Runtime.InteropServices` (`LibraryImport`, `MarshalAs`), `System.IO` (Path). Note: `[LibraryImport]` requires the containing type be `partial` (it is) and emits marshaling stubs at compile time.

- [ ] **Step 2: App wiring**

In `App.xaml.cs`:

1. Add field + guard creation as the FIRST statements of `OnStartup`:

```csharp
        private static SingleInstanceGuard? _guard;
```

```csharp
            var awaitInstance = e.Args.Contains("--await-instance", StringComparer.Ordinal);
            _guard = new SingleInstanceGuard(awaitInstance);
            if (!_guard.IsFirstInstance)
            {
                _guard.ActivateFirstInstanceWindow();
                Shutdown();
                return;
            }
```

(needs `using TaskManager.Infrastructure;` — adjust to whatever namespace imports exist.)

2. Replace `Restart()`:

```csharp
        internal static void Restart()
        {
            var currentExecutablePath = Environment.ProcessPath;
            using var successor = System.Diagnostics.Process.Start(currentExecutablePath!, "--await-instance");
            _guard?.Dispose(); // successor waits for release (handoff mode)
            Application.Current.Shutdown();
        }
```

3. Replace Task 2's provisional `RelaunchElevated()`:

```csharp
        /// <summary>
        /// Relaunches the app requesting elevation. A declined UAC prompt is a silent
        /// no-op (mutex untouched); a successful spawn hands the mutex off and exits.
        /// Any other failure propagates to the caller's error handling.
        /// </summary>
        internal static void RelaunchElevated()
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--await-instance",
            };

            System.Diagnostics.Process.Start(psi);
            _guard?.Dispose();
            Application.Current.Shutdown();
        }
```

(`Win32Exception` 1223 from a declined prompt propagates to the VM's `_errorHandler.Guard*` — add a cancel-specific swallow HERE instead, because an error dialog for pressing "No" on UAC is wrong UX:

```csharp
            try
            {
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return; // user declined elevation: stay running, still protected
            }
```

placed around the Start call; the dispose/shutdown lines follow outside the try.)

4. `OnExit` (added in an earlier wave): append `_guard?.Dispose();` AFTER the existing `_serviceProvider.Dispose();` line.

- [ ] **Step 3: Guard tests**

`tests/TaskManager.UnitTests/Infrastructure/SingleInstanceGuardTests.cs`:

```csharp
using TaskManager.Infrastructure;

namespace TaskManager.UnitTests.Infrastructure
{
    public class SingleInstanceGuardTests : IDisposable
    {
        // unique-per-run name would be ideal, but the name is baked into the guard;
        // tests therefore serialize via a global lock file-free approach: xunit v3
        // runs classes in parallel, so guard tests disable parallelization per class
        // via collection definition if needed. Keep each test self-contained.

        [Fact]
        public void FirstConstruction_OwnsMutex()
        {
            using var guard = new SingleInstanceGuard(waitForExistingRelease: false);
            guard.IsFirstInstance.ShouldBeTrue();
        }

        [Fact]
        public void SecondConcurrentConstruction_IsNotFirstInstance()
        {
            using var first = new SingleInstanceGuard(waitForExistingRelease: false);
            using var second = new SingleInstanceGuard(waitForExistingRelease: false);

            first.IsFirstInstance.ShouldBeTrue();
            second.IsFirstInstance.ShouldBeFalse();
        }

        [Fact]
        public void Dispose_AllowsImmediateReacquisition()
        {
            var first = new SingleInstanceGuard(waitForExistingRelease: false);
            first.Dispose();

            using var second = new SingleInstanceGuard(waitForExistingRelease: false);
            second.IsFirstInstance.ShouldBeTrue();
        }

        [Fact]
        public async Task HandoffMode_WaitsForPredecessorRelease()
        {
            var first = new SingleInstanceGuard(waitForExistingRelease: false);

            var successorTask = Task.Run(() => new SingleInstanceGuard(waitForExistingRelease: true));
            // give the successor a moment to enter its wait
            await Task.Delay(200);
            successorTask.IsCompleted.ShouldBeFalse(); // still waiting on the held mutex

            first.Dispose(); // predecessor releases

            using var successor = await successorTask;
            successor.IsFirstInstance.ShouldBeTrue();
        }

        public void Dispose()
        {
            // best-effort cleanup so a failed test does not poison later runs
            try
            {
                using var g = new SingleInstanceGuard(waitForExistingRelease: true);
                g.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
```

NOTE on parallelism: these tests contend on one real global-per-session mutex. If the suite runs test classes in parallel and another class touches the guard (none does today), or these four interleave badly, add `[Collection("SingleInstance")]` to this class AND define the collection only here — report whichever arrangement you needed. The `HandoffMode` test's `ShouldBeFalse` mid-wait assertion assumes the mutex is still held at +200 ms; if that proves flaky, lengthen to 500 ms and report.

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.UnitTests
```

Zero warnings; suite green twice (expected ≈150-152 depending on Task counts).

Manual smoke (report each outcome honestly; programmatic close allowed):
1. Launch app → launch second instance → original window foregrounded, second exits instantly.
2. Minimize original → second launch → original restores + foregrounds.
3. Settings → switch language (triggers restart) → app restarts cleanly (handoff works).
4. Status bar → relaunch button → UAC appears → Cancel → app continues running; repeat → Accept → elevated instance takes over.
5. About → version/commit match `InformationalVersion`.

- [ ] **Step 5: Commit**

```bash
git add src/TaskManager tests/TaskManager.UnitTests
git commit -m "feat: single-instance guard with activation and elevated relaunch handoff"
```

---

## Self-Review Record

- **Spec coverage:** §2 guard (modes/handoff/activation/app wiring/restart-relaunch cooperation) → Task 4; §3 diagnostics types + catalog hooks + view layer + elevation service → Tasks 1–2; §4 About → Task 3; §5 error table rows covered (abandoned-mutex via createdNew/catch in guard Step 1; 1223 swallow; no-window silent exit; pre-tick dashes in `OutcomeText` fallback); §6 testing items mapped: catalog diagnostics (T1 S3), AboutInfo (T3 S2), guard mutex semantics (T4 S3), VM/elevation mapping via converter tests + manual smoke (T2 S6/S7 — VM-construction-heavy mapping intentionally left to manual + future D2, noted here as deliberate); §8 AC1–AC6 → manual smoke list T4 S4 + automated evidence. ✔
- **Placeholder scan:** Task 2's provisional `RelaunchElevated` is explicitly staged with its replacement defined in Task 4 (both bodies shown in full); helper-resolution contingencies give concrete actions; no TBD/TODO text. ✔
- **Type consistency:** `RefreshDiagnostics(double, RefreshOutcome, DateTimeOffset)` consistent T1↔T2; `IElevationService.IsAdministrator` consumed identically; `ShowAbout()` declared/consumed; `--await-instance` spelled identically in Restart/Relaunch/guard docs; `SingleInstanceGuard(bool waitForExistingRelease)` matches all test constructions. ✔
