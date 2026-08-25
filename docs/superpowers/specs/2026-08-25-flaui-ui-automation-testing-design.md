# FlaUI UI-Automation Test Suite — Design

- **Date:** 2026-08-25
- **Status:** Approved (brainstorming session)
- **Constraint (product owner):** running tests must NEVER open windows or processes visible on the developer's PC. Verified baseline: existing unit/integration suites are hermetic (integration victims are invisible `cmd.exe /c ping` with `CreateNoWindow=true`).

---

## 1. Decisions

| Question | Decision |
|---|---|
| Execution venue | **CI-only + explicit local opt-in.** The suite lives OUTSIDE the solution so no default `dotnet test` can reach it (structural guarantee beats policy). CI runs it on GitHub's ephemeral Windows VM; local runs happen only when a human deliberately invokes the project — and those windows are visible *by consent*. Off-screen positioning machinery explicitly rejected (YAGNI; non-zero visibility risk for a workflow that doesn't exist yet). |
| Framework | FlaUI (`FlaUI.UIA3`) driving the real built exe. In-box `System.Windows.Automation` rejected per product owner (FlaUI requested; better API/retry ergonomics). |
| App data isolation | Tests never touch real `%LOCALAPPDATA%`: three environment redirects honored by the composition root (settings dir, log dir, instance-name suffix). |
| Assertion style | Behavioral only — window titles, visible labels (read from the app's own localized `Strings` resource), dialog presence, process liveness. Never element trees, internal state, or pixel layout. |

## 2. Suite Architecture

**Project:** `tests/TaskManager.UiAutomationTests/TaskManager.UiAutomationTests.csproj`
- NOT referenced by `TaskManager.slnx`; invoked explicitly by path.
- `ProjectReference` to `src/TaskManager/TaskManager.csproj` — building the test project builds the exe; exe path resolved from the referenced assembly's location (`App` type assembly, `.dll`→`.exe`).
- Packages (via Central Package Management entries): `FlaUI.UIA3`, repo-standard `Shouldly`, `xunit.v3` stack (MTP applies via `global.json`).
- Runs sequentially under one collection; each dialog-touching test cleans up in `finally`.

### Harness components (one responsibility each)

**`AppSession`** (collection fixture — one app for the whole run):
1. Creates throwaway temp dirs; generates a run-unique instance suffix.
2. Launches the exe through FlaUI with env vars set (see §4).
3. Asserts the SPAWNED PID owns a window ~2 s after launch — if the single-instance guard bounced the launch, the test fails loudly with exit code + redirected-log tail instead of silently automating the wrong process.
 4. Wraps `Application` + `UIA3Automation` lifetime; teardown closes, kills, deletes temp dirs.

 5. Redirect proof: before any test runs, the fixture asserts the redirected SETTINGS dir now contains the freshly written settings file - evidence the env redirect took effect (AC5).
**Page objects** (`Pages/`): `MainWindowPage`, `SettingsDialogPage`, `AboutDialogPage`, plus a `MessageBoxHelper`. All lookups go through FlaUI retry waits (`Retry.WhileNull`, 10 s ceilings, no `Thread.Sleep`). Finders key on window title, visible content/menu labels, or localized strings pulled from `TaskManager.Resources.Languages.Strings` via the project reference — assertions survive relabeling only when labels change intentionally.

**`VictimFactory`**: copies `cmd.exe` to `%TEMP%\<guid>.exe`, starts it hidden (`CreateNoWindow`, `/c ping -n 60 …`), exposes unique process name + PID; guaranteed kill in cleanup. Gives terminate/priority tests a collision-proof, safely-killable row among hundreds of real processes.

## 3. Test Inventory

Happy paths:

| # | Test | Observable assertion |
|---|---|---|
| H1 | Launch shows live main window | Window titled `Task Manager`; strip reports `>0` processes; interval text matches Low-frequency template |
| H2 | About shows stamped identity | Version equals file-metadata version-part; commit equals sha-part; OK closes |
| H3 | Settings round-trip | Dialog opens; frequency set to Paused via visible label; Save; strip shows Paused label |
| H4 | Export opens & cancels | Export dialog appears; Cancel returns to live main window |
| H5 | Terminate victim (happy) | Victim row selected → Terminate → confirm → victim process exits within timeout; row gone within a 12 s retry ceiling (one Low-frequency tick), with no manual-refresh automation dependency |
| H6 | Terminate victim (cancel) | Same flow with Cancel → victim still alive |
| H7 | Priority on victim | Victim selected → priority dialog → Idle applied → nothing else affected |

Sad paths:

| # | Test | Observable assertion |
|---|---|---|
| S1 | Terminate with no selection | Localized validation message box appears; dismissed; app healthy |
| S2 | Second-instance launch (E2E of H2) | Duplicate exe exits ~immediately with code 0; original stays responsive |

**Deliberately NOT tested here:** killing real system processes; UAC click-through; language-switch restart loop (handoff mechanics covered by S2 + unit tests); visual layout/pixels; behavior already hermetic (parsers, diff engine, diagnostics math, logger internals).

## 4. Production Changes (testability hooks)

All small, all default-inert:

1. **Settings redirect:** `TASKMANAGER_SETTINGS_DIR` env var → composition root constructs `JsonSettingsStore(dir, logger)`; unset ⇒ today's `%LOCALAPPDATA%` path.
2. **Log redirect:** `TASKMANAGER_LOG_DIR` → `FileLoggerProvider(dir)`; unset ⇒ today's path.
3. **Instance-name suffix:** `TASKMANAGER_INSTANCE_NAME` → `SingleInstanceGuard` mutex becomes `Local\TaskManager.SingleInstance.<suffix>`; unset ⇒ unchanged name. Guarantees opt-in/CI runs never collide with — or foreground — a real instance.
4. **Window title:** placeholder `"MainWindow"` → `"Task Manager"` (FlaUI's primary window selector; also fixes a long-standing cosmetic placeholder).

Failure story: bounced launch ⇒ loud fail-fast with exit code + redirected-log tail; every test dismisses open dialogs in `finally` so failures don't cascade; CI publishes the redirected log dir as an artifact on failure — the owner's diagnostic window into invisible runs.

## 5. CI Wiring & Local Opt-in

`ci.yml` order: build slnx → build UiAutomationTests project explicitly → unit tests → UIA step invoking the out-of-solution csproj by path. GitHub windows-latest runners provide a real interactive session, so UIA works and windows exist only on the ephemeral VM.

README "Development notes" gains the exact local opt-in command (the same step, invoked manually).

Known-flake policy: no automatic retries; a red CI run is investigated and root-caused (framework-level rerun allowed once locally during debugging only).

## 6. Out of Scope

- D2 diagnostics-window automation; L3 log-surfacing UI
- UAC accept/decline E2E flows; language-switch restart E2E
- Performance/benchmark passes; visual regression (pixel) testing
- Parallel UI-test execution (single sequential collection is deliberate)

## 7. Acceptance Criteria

1. Solution-level build/test commands cannot execute the FlaUI suite (project absent from `.slnx`).
2. The documented local opt-in command works; it is the ONLY way the suite runs on a developer PC.
3. CI executes the suite after unit tests on `windows-latest` and passes; failed runs publish app logs.
4. All ten inventory tests (H1–H7, S1–S2) exist and pass in CI.
5. During any test run, zero reads/writes hit the real `%LOCALAPPDATA%\TaskManager` settings/logs (verified by redirect assertions in `AppSession`).
6. New packages are confined to the UiAutomationTests project; CPM entries added to `Directory.Packages.props`.
7. Assertions reference visible text/titles/process liveness only — grep of the suite finds no automation IDs tied to internal layout beyond the agreed title contract.
