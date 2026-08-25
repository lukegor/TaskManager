# Status Bar, About Dialog & Single Instance — Design

- **Date:** 2026-08-25
- **Status:** Approved (brainstorming session)
- **Scope items:** D1 (status bar: polling health + elevation), D3 (About dialog), H2 (single-instance guard) from `docs/engineering-infrastructure-backlog.md` — Wave 3 minus L3.
- **Depends on:** B3 version stamping (`InformationalVersion = <version>+<sha>` — landed in Wave 1 CI); L2 refresh pipeline (durations measurable via `TimeProvider`).

---

## 1. Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Elevation indicator | Display **plus** relaunch-elevated action (UAC prompt; cancel = silent no-op) |
| Second-launch UX | Activate the existing window (Win32 `SetForegroundWindow`); no message box |
| Diagnostics data flow | Catalog-owned snapshot pushed via INPC — same forwarding pattern as `ProcessCount` (Approach A); no timers, no extra services |
| Activation mechanism | `Process.GetProcessesByName(<current exe base name>)` → `MainWindowHandle`, never the window-title string |
| Mutex scope | `Local\` namespace — one instance per user session is legitimate |

## 2. H2 — SingleInstanceGuard

New `src/TaskManager/Infrastructure/SingleInstanceGuard.cs`:

```csharp
internal sealed class SingleInstanceGuard : IDisposable
{
    public SingleInstanceGuard(bool waitForExistingRelease);  // see modes below
    public bool IsFirstInstance { get; }
    public void ActivateFirstInstanceWindow();                // best-effort, second instance only
    public void Dispose();
}
```

- **Name:** `Local\TaskManager.SingleInstance` (per-session scope).
- **Normal mode** (`waitForExistingRelease == false`, plain launches): `new Mutex(true, name, out createdNew)`; `createdNew == true` ⇒ first instance; otherwise second-instance path. The `createdNew` pattern is immune to `AbandonedMutexException` from crashed predecessors.
- **Handoff mode** (`waitForExistingRelease == true`, passed `--await-instance` command-line argument): construct non-owning, then `WaitOne(TimeSpan.FromSeconds(10))` for the predecessor to release during restart/relaunch. `AbandonedMutexException` from a crashed predecessor counts as acquired. Timeout ⇒ fall back to second-instance behavior (best effort).
- **Activation:** enumerate `Process.GetProcessesByName(<current executable base name without extension>)`, skip own PID, take the first process with `MainWindowHandle != 0`; `ShowWindow(handle, SW_RESTORE)` when iconic, then `SetForegroundWindow(handle)`. P-Invoke declarations stay in this class (or an adjacent interop static) — small, private, `[LibraryImport]`-style.
- **App wiring:** guard constructed in `App.OnStartup` **before container build**, stored in a static field. Flow:

```
OnStartup(e):
  args contain --await-instance ?
        ── yes ─▶ guard = new(true)
        ── no  ─▶ guard = new(false)
  !guard.IsFirstInstance ─▶ ActivateFirstInstanceWindow(); Shutdown(); return
  ... existing startup ...
```

- **Restart cooperation:** `App.Restart()` (language switch) and the new `App.RelaunchElevated()` pass `--await-instance` to the successor AND dispose the guard only after `Process.Start` succeeds — the successor waits for release, the predecessor keeps protection until the successor provably exists. On failed spawn (restart path), shutdown proceeds regardless; on failed elevated spawn, the app stays running protected.

### Relaunch-elevated

```csharp
// App
internal static void RelaunchElevated()  // called from MainWindowViewModel command
```

- `ProcessStartInfo { FileName = Environment.ProcessPath, UseShellExecute = true, Verb = "runas", Arguments = "--await-instance" }`
- Success (`Process.Start` returns) ⇒ dispose guard ⇒ `Shutdown()`.
- `Win32Exception` with `NativeErrorCode == 1223` (UAC declined) ⇒ swallow, log Information, keep running **with the mutex still held** (spawn never happened).
- Other exceptions ⇒ message-service error, keep running.

Command placement: `MainWindowViewModel.RelaunchElevatedCommand`, following the existing `OpenSettings → App.Restart()` precedent for process-level actions invoked from VMs.

## 3. D1 — Status Bar

### Diagnostics state (catalog-owned)

Presentation-layer types beside the catalog (UI telemetry, deliberately not Domain):

```csharp
public enum RefreshOutcome { Ok, Skipped, Failed }

public sealed record RefreshDiagnostics(
    double LastDurationMs,
    RefreshOutcome Outcome,
    DateTimeOffset CompletedAt);
```

`ProcessListCatalog` additions (all INPC, all raised through the existing `_dispatcher.Invoke` discipline so bindings always see UI-thread events):

| Member | Set where |
|---|---|
| `LastRefresh` (`RefreshDiagnostics?`) | Ok: end of `RefreshCoreAsync` with `TimeProvider.GetElapsedTime`; Skipped: gate-busy early-return in `SafePollingRefreshAsync` (keeps previous `LastDurationMs`); Failed: the existing warning-catch in `SafePollingRefreshAsync` (keeps previous ms) |
| `IsPollingPaused` (`bool`) | `OnSettingsChanged` (`frequency == Paused`); also true until polling starts |

Manual user refreshes flow through `RefreshCoreAsync` and update diagnostics naturally.

### View layer

- Row 3 of `MainWindow.xaml` becomes a single-row strip: `{count} processes · every {n}s | Paused` · outcome chip (small colored dot: green Ok, gray Skipped, red Failed — localized tooltip explains each) · right-aligned elevation chip.
- `MainWindowViewModel` forwards `LastRefresh` / `IsPollingPaused` by extending its existing `catalog.PropertyChanged` subscription block, exposes `RelaunchElevatedCommand`, and maps elevation to badge text via injected `IElevationService`.
- Chip colors map through a small `IValueConverter` (`RefreshOutcome → Brush`) in the existing `UI/Converters` folder — brushes stay out of the VM.
- New resx strings (EN/PL, existing x:Static pattern), exact keys: `StatusPaused`, `StatusAdministrator`, `StatusStandard`, `StatusRelaunchAsAdmin`, `StatusOutcomeOk/Skipped/Failed` (tooltips), `HelpMenu`, `AboutMenu`.

### Elevation service

```csharp
// TaskManager.Abstractions
public interface IElevationService { bool IsAdministrator { get; } }
```

Implementation computes `WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)` once, lazily; registered singleton in the UI registration module. When standard: elevation chip renders as a button (tooltip = relaunch string) wired to `RelaunchElevatedCommand`; when admin: plain text chip, command unused.

## 4. D3 — About Dialog

- `IWindowService.ShowAbout()` added; `WindowService.CreateAboutDialog()` constructs `AboutWindow` directly — **no view-model**: the dialog is static content (the settings-style close-relay machinery is unnecessary; OK button closes in code-behind).
- New `Help` top-level menu containing `About`, bound to `OpenAboutCommand` on the main VM (mirrors `OpenSettingsCommand`).
- Content stack: product name, `Version <x.y.z>`, `Commit <sha>`, license line referencing `LICENSE.txt` (wording stays generic — no license type asserted), hyperlink to the repository (`https://github.com/lukegor/TaskManager`, opened via `RequestNavigate` handler), OK button (`IsDefault`).
- Version parsing lives in an internal static helper so it is unit-testable:

```csharp
namespace TaskManager.Services
internal static class AboutInfo
{
    public static string Version { get; }   // parsed once from
    public static string Commit { get; }    // AssemblyInformationalVersion of the entry assembly
    internal static (string Version, string Commit) Parse(string informationalVersion);
}
```

`Parse("0.7.0+1a2b3c")` → `("0.7.0", "1a2b3c")`; missing `'+'` ⇒ commit empty; malformed input tolerated (whole string becomes Version).

## 5. Error Handling Summary

| Failure | Behavior |
|---|---|
| Second instance races a shutting-down first | Handoff-mode wait (≤10 s) resolves it; timeout falls back to activate-and-exit |
| Predecessor crashed (abandoned mutex) | Treated as acquired; app starts normally |
| No activatable window found | Second instance exits silently |
| UAC declined (1223) | Silent no-op; mutex untouched; app continues |
| Elevated/restart spawn fails otherwise | Message-service error; app continues protected |
| Diagnostics queried before first tick | Strip shows placeholder dashes |

## 6. Testing

Hermetic unit tests (existing harness patterns — ScriptedEnumerator, FakeTimeProvider, NSubstitute):

1. **Catalog diagnostics:** Ok outcome with non-negative duration after `LoadForTestAsync`; Skipped via concurrent-gate occupancy; Failed via throwing enumerator script; `IsPollingPaused` flips via `SettingsChangedTo(Paused)`; INPC raises observed for both members.
2. **AboutInfo.Parse:** with `'+'`; without `'+'`; empty string; multiple `'+'` (split on first occurrence).
3. **SingleInstanceGuard:** real-mutex in-proc — second construction reports `IsFirstInstance == false`; Dispose allows immediate re-acquisition; handoff-mode construction succeeds while another handle holds the mutex within the wait window. Interop activation is manual-smoke territory.
4. **VM mapping:** substituted `IElevationService` drives badge text/command wiring assertions.

Manual smoke checklist: second launch foregrounds the original (incl. minimized restore); language-switch restart completes; relaunch-elevated shows UAC, cancel leaves app running, accept hands off cleanly; About shows version+commit matching the built `InformationalVersion`; status chips update live across paused/manual-refresh/error injection.

## 7. Out of Scope

- Full F5 elevation feature (access-denied taxonomy, per-process messaging) — only the relaunch action lands here
- D2 diagnostics window; log-surfacing UI (L3 explicitly excluded by product owner)
- Cross-session single instancing; installer-level activation protocols

## 8. Acceptance Criteria

1. Second launch exits immediately and the first window is foregrounded (restored if minimized); plain double-launch never waits.
2. Both restart paths (language switch, elevated relaunch) complete without deadlocks or second-instance misfires; UAC cancellation leaves the app running and protected.
3. Status strip shows live process count, interval-or-paused, and last-refresh outcome; Skipped/Failed/Paused states are unit-pinned.
4. Elevation chip displays correctly in both states and offers relaunch exactly when standard.
5. About dialog renders version and commit matching the built `InformationalVersion`, plus license reference and working repo link.
6. All new strings exist in EN and PL; suite green under warnings-as-errors; zero new package dependencies.
