# Investigation: FlaUI MenuItem Invoke silently no-ops routed commands

- **Status:** OPEN - 3 tests disabled, root cause identified at the framework level,
  recommended fix path chosen but not yet implemented
- **Opened:** 2026-08-26
- **Repo:** Task Manager (WPF .NET 10 process explorer)
- **Branch:** `build/engineering-guardrails`
- **Audience:** AI agents picking this up. Read top-to-bottom before touching anything.

---

## 1. TL;DR for the next agent

Driving WPF `MenuItem`s purely via UIA patterns (FlaUI) finds and "invokes" them
successfully - `Invoke()` returns without error, the element is the LIVE popup
instance - but the `RoutedCommand` behind the item **never executes**. Real mouse
clicks on the same items work perfectly. Five distinct fixes were attempted; all
failed identically. Conclusion: this is a WPF menu/command-routing limitation
under headless UIA invocation, not a bug in our test code.

**Recommended path forward (owner-approved direction):** stop driving menus.
Add `AutomationProperties.AutomationId` to the toolbar Terminate/Priority
buttons in production XAML and invoke THOSE (`ButtonBase` + `InvokePattern` ->
command routing is reliable). Details in section 8.

**Hard constraints that shaped everything (do not violate):**

| Constraint | Reason |
| --- | --- |
| Never launch GUI/windows on the developer PC during automated work | Owner explicitly forbade after witnessing test launches |
| Never use real input simulation (`Mouse.*`, `Keyboard.*`) | Moves user's real cursor / sends real keys ("fucking stop it") |
| Suite is structurally CI-only | Out-of-solution csproj + deleted local runner script |
| No file access outside repo workspace (no `%TEMP%` reads/writes from tools) | Owner rejected TEMP access mid-session |
| All diagnostics output into `artifacts/diag/` inside the repo | gitignored, accessible |

## 2. Environment facts

- FlaUI.UIA3 **5.0.0** (upgraded from 4.0.0 mid-investigation; only breaking
  change was nullable annotation on `GetMainWindow`)
- xunit.v3 3.2.2 on Microsoft.Testing.Platform **1.9.1**
  - MTP 1.9.x rejects `--nologo` on test invocations (exit 5)
  - MTP 1.9.x has NO `--timeout` option and NO `--filter-class`; only
    `--filter-namespace` etc.
  - `dotnet test ... | Select-Object` inline piping can HANG the agent shell:
    orphaned children (testhost/app) inherit stdout pipes and keep them open
    after exit -> stream readers wait for EOF forever. Always use the detached
    pattern (section 7).
- Dev machine OS culture: **pl-PL**. App reads saved settings language; fresh
  profiles fall back to OS culture -> app boots Polish while test host resolves
  `Strings.*` as English unless locale pinned. Fix in place: fixture seeds an
  English `settings.json` into redirected dir AND pins host culture to en-US.
- Build quirk discovered late: **`dotnet build` reported success without
  recompiling edited sources** (stale outputs across several iterations). Every
  "fix attempt" ran against a stale binary until a forced `--no-incremental`
  build surfaced real compile errors. ALWAYS verify binary freshness
  (timestamp or marker-string search inside the built DLL) when results look
  impossible.

## 3. What works under FlaUI 5 (all green)

| Test | Notes |
| --- | --- |
| Launch_ShowsLiveMainWindow | status strip: count > 0, interval label, elevation button |
| About_ShowsStampedVersionAndCommit | Help->About opens; version+commit asserted; OK closes |
| Settings_RoundTrip_PublishesPausedThenRestores | combo select by index/label; Save; strip updates |
| Export_OpensAndCancels | dialog open + WindowPattern close |
| Priority_AppliesToVictimOnly | dialog confirm via cross-window button sweep; asserts dialog self-closes |
| SecondInstance_ExitsImmediately_AndOriginalStaysAlive | guard bounce E2E |

Menu navigation to About/Settings works because those leaves are found and
invoked... note however section 6: after the final InvokeMenu rework, even
About began failing intermittently - see open question O-3.

## 4. What fails (the disabled trio)

All three share one signature:

```
expected dialog 'Confirm'/'Error' within 20 s; open windows: ['Task Manager']
```

Sequence per test: victim row found (unique-named sacrificial cmd.exe copy),
row selected via SelectionItemPattern, Management menu expanded, leaf 'Terminate
processes' FOUND on the live popup, `Invoke()` executed without error -
then **no MessageBox ever materializes** (20 s poll), i.e. the routed command
never ran. Identical outcome for Happy/Cancel/WithoutSelection variants.

## 5. Attempted fixes (all failed identically)

| # | Hypothesis | Change | Outcome |
|---|---|---|---|
| 1 | Stale pre-expansion container preferred over live popup | Prefer cross-window sweep FIRST, parent-subtree fallback | Same failures |
| 2 | Search happens before submenu containers exist | Expand parent BEFORE searching leaf; removed duplicate post-assign expand | Same failures |
| 3 | Retry races | Whole invoke retried once when expected dialog missing within its own window | Deterministic no-op, not a race |
| 4 | Locale mismatch (app Polish vs host English) | Seed English settings.json pre-launch; pin host culture en-US | Menu labels now match; still silent no-op |
| 5 | Row-selection semantics (grid selection vs checkbox-bound VM state) | Verified two-way bindings (row style + checkbox both push `ProcessItem.IsSelected`); commands have no CanExecute gate | Not the blocker |

Also verified NOT the cause: element staleness after expand (cross-window sweep
returns live popup instance), IsExpanded state handling, FlaUI Launch env
delivery (bypassed via Process.Start + Attach-by-PID), DataGrid virtualization
(disabled via TASKMANAGER_UITEST=1 hook in BetterDataGrid ctor).

## 6. Leading explanation (unproven but consistent)

WPF `MenuItemAutomationPeer.Invoke()` raises the click through the automation
peer, but command EXECUTION for `RoutedCommand`s depends on
`CommandTarget`/focus-context that pattern invocation does not establish the
same way a physical click does. With the popup reparenting items into a separate
HWND, the invoked peer's routing context resolves to nothing and the command
silently drops. This matches: Invoke succeeds, IsSupported=true, element is the
correct named item, zero exceptions, zero side effects.

If you need certainty before implementing section 8: write a 20-line scratch WPF
app with one MenuItem bound to a RoutedCommand and confirm the same no-op via
FlaUI - that isolates WPF-vs-app conclusively in ~15 minutes.

## 7. Recommended next steps (ranked)

1. **Toolbar AutomationId route (recommended, owner-endorsed direction):**
   - Production: add `AutomationProperties.AutomationId="TerminateButton"` /
     `"PriorityButton"` to the toolbar buttons in MainWindow.xaml (accessibility win).
   - Tests: find window descendant by AutomationId, `InvokePattern.Invoke()`.
     ButtonBase->command routing via UIA Invoke is reliable (unlike MenuItem).
   - Keep the three tests skipped until this lands, then un-skip and swap
     `OpenTopLevelLeaf(...)` calls for the AutomationId invocations.
2. Alternative: drive termination through keyboard on CI only (relax the input
   grep-gate for `CI=true` runs) - rejected for now because it reintroduces
   input-simulation policy complexity.
3. Alternative: drop the three UI-level terminate tests; cover the flow with
   hermetic VM tests (selection logic is already pinned there).

## 8. Reproduction & tooling

```powershell
# build + run the whole UIA suite locally (OPENS WINDOWS - requires owner consent)
$exe = "tests\TaskManager.UiAutomationTests\bin\Debug\net10.0-windows\TaskManager.UiAutomationTests.exe"
$p = Start-Process -FilePath $exe -RedirectStandardOutput artifacts\diag\run.log `
     -RedirectStandardError artifacts\diag\run.err -PassThru -NoNewWindow
# poll liveness; HARD-KILL tree on deadline ($p.Kill($true))
```

- NEVER run this suite inline via `dotnet test ... | tail` from an agent shell:
  inherited stdout handles from testhost/app children keep pipes open and hang
  the agent indefinitely after process exit.
- Diagnostics land in `artifacts/diag/` (repo-local, gitignored). Never `%TEMP%`.
- Set `$env:TMUITEST_DIAG="1"` for extra row/menu diagnostics (currently prints
  parent children + per-window menu-item enumeration on lookup failure).
- Binary freshness check after every build:
  `Select-String -Path <dll> -Pattern "<new-string>" -Quiet`

Key files:

| File | Role |
|---|---|
| `tests/TaskManager.UiAutomationTests/AppSession.cs` | fixture: launch/attach, redirects, proof, teardown |
| `tests/TaskManager.UiAutomationTests/Pages/MainWindowPage.cs` | behavioral lookups incl. InvokeMenu |
| `tests/TaskManager.UiAutomationTests/BehaviorTests.cs` | the disabled trio + passing siblings |
| `src/TaskManager/UI/Controls/BetterDataGrid.cs` | TASKMANAGER_UITEST virtualization-off hook |
| `src/TaskManager/App.xaml.cs` | startup marker log + LaunchGUI ordering (init before Show) |
| `scripts/run-uia-tests.ps1` | DELETED - local runner removed with CI-only ruling |

## 9. Related history (same session, same discipline)

- Startup empty-grid fix: initialization now kicks off BEFORE `Show()` in
  `App.LaunchGUI` (owner-reported regression, root-caused to ordering not polling).
- Async logger adoption details, overflow-detection design, and the earlier
  MenuItem-free decision record live in `docs/superpowers/specs/` and the
  companion plans under `docs/superpowers/plans/`.
