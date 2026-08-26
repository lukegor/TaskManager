# WPF FlaUI E2E Testing - Agent Playbook

Operational knowledge from adopting FlaUI on a real WPF app (process explorer,
.NET 10, xunit.v3/MTP). Written for AI agents planning or executing such work
in any repository under the OPERATING FRAMEWORK. Companion to
`wpf-ui-testing-decision-rule.md`: that rule picks between headless seams (A)
and in-process STA views (B); **FlaUI is strategy C - driving the real packaged
exe end-to-end through Windows UI Automation** - and everything below was paid
for in practice.

---

## 1. The cardinal rule: FlaUI input simulation is REAL input

`Mouse.MoveTo`, `Mouse.RightClick`, `Mouse.Scroll`, `Keyboard.Press` do not
simulate anything. They inject into the user's actual input pipeline: the
cursor moves on their desktop, synthetic keystrokes land in whatever window
has focus, and the application window opens visibly. If the product owner says
"never touch my machine", then:

- **Decide the venue before writing a single test.** Default answer:
  **CI-only, enforced structurally** - put the suite in its own csproj that is
  NOT referenced by the solution. Nothing else provides a guarantee; policies
  ("only run it deliberately") erode the moment an agent tries to be helpful.
- **Local execution is prohibited entirely**, not "opt-in". Opt-in flags are
  still one careless invocation away from hijacking the user's session.
- If a mid-execution violation occurs, stop immediately, restructure, and
  record the ruling durably (spec banner + README). Do not apologize-and-continue
  under the original rules.

Structural enforcement checklist:

- Suite project absent from `.slnx`; invoked only by path (CI step / documented command).
- Zero `Mouse.` / `Keyboard.` / `VirtualKeyShort` references anywhere in the
  suite - grep this as a gate.
- Dialog/menu interaction via UIA patterns only (see section 5).

## 2. Venue and isolation

- Runner VMs (GitHub `windows-latest`) have real interactive sessions: UIA works,
  windows appear on the ephemeral VM, nobody sees them. That is where the suite lives.
- Isolate app data with environment-variable redirects read by the composition
  root (`TASKMANAGER_SETTINGS_DIR`, `TASKMANAGER_LOG_DIR`, instance-name suffix
  appended to any single-instance mutex name). Unset env vars MUST mean
  byte-for-byte production behavior.
- Prove the redirect took effect before tests run (see section 6), or the proof
  will silently rot.
- Suffixed (automation) instances must never activate foreign windows: guard
  activation behind "is this the unsuffixed production namespace".

## 3. FlaUI 4.x API facts (each one broke against a plan drafted from memory)

| Drafted-from-memory assumption | Reality (FlaUI.UIA3 4.0.0) |
| --- | --- |
| `Application.Launch(path, Action<ProcessStartInfo>)` | Does not exist. Build an explicit `ProcessStartInfo` (set `UseShellExecute=false`, then fill `EnvironmentVariables`) and `Application.Launch(psi)` |
| `app.PID` | `app.ProcessId` |
| Grid rows are `ControlType.Row` | WPF `DataGridRow` surfaces as `ControlType.DataItem` |
| `SelectionItemPattern.Toggle()` | Does not exist. Use `Select()` / `AddToSelection()` / `RemoveFromSelection()` |
| `Keyboard.Press(VirtualKeyCodes.X)` | Enum is `VirtualKeyShort` (`FlaUI.Core.WindowsAPI`) - but see section 5: avoid keyboard entirely |
| `[LibraryImport]` just works | Requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (SYSLIB1062) and consistent `[return: MarshalAs(UnmanagedType.Bool)]` on all imports |

Rule for agents: a plan's code blocks are hypotheses. Verify every external-API
line against the pinned package before trusting it, and report each deviation -
deviation reports are workflow output, not failures.

## 4. WPF DataGrid virtualization trap

With UI virtualization on, **the UIA tree contains only viewport-realized rows**
(typically ~20 of several hundred). Any "find the row" helper must page:

```
loop until deadline:
    scan realized DataItems for a name containing target -> return hit
    advance scrollbar via RangeValue.SetValue(value + LargeChange)   // headless-safe
    (never Mouse.Scroll / wheel - that is real input)
    small settle sleep
reset scroll position between sweeps
```

Additional traps:

- Rows vanish/reappear mid-refresh while the list updates - wrap every row
  access in try/catch and rely on the sweep loop to retry.
- `GetClickablePoint()` throws for virtualized (off-screen) rows.
- Prefer unique-named sacrificial processes over searching for common names
  (`cmd.exe` collides with dozens of rows): copy cmd.exe to
  `%TEMP%\<guid>.exe` and launch that; the row name becomes collision-proof.

## 5. Dialog dismissal without touching input devices

Standard MessageBoxes and dialogs are dismissible purely through patterns:

| Intent | Mechanism |
| --- | --- |
| Accept (OK/default) | Find the dialog's first `ControlType.Button` descendant and invoke it. Every box raised by the app under test had OK as the first/default button - verify per dialog. |
| Cancel / close | `element.AsWindow().Patterns.Window.Pattern.Close()` (WM_CLOSE semantics: acts as Cancel on OKCancel boxes, closes plain dialogs) |
| Finding the box | Top-level windows of the launched process, matched by title (captions come from the app's own localized strings resource) |

Never drive OS-localized buttons by label ("OK"/"Anuluj") and never send
Enter/Escape - both are locale- and focus-dependent; the keyboard variant also
hijacks the user's typing when run locally.

## 6. Observable-signal design for launch fixtures

Traps actually hit:

- Settings stores that persist lazily (write only on Save) give **no early
  signal** that an env redirect took effect - a "settings file exists" proof
  fails forever even though redirection works.
- Apps that log nothing at Information level during a healthy startup produce
  **zero log lines until graceful shutdown** - which a hard-killed fixture never
  performs. Absence-of-output proofs are unsound.
- Shared fixed directories survive hard kills: a stale file makes the next
  run's existence-check pass vacuously. Pre-clean shared dirs before launch,
  or make paths unique per run.

Fix that also benefits users: add an explicit startup marker
(`LogInformation("Application starting")` at composition-root completion).
Then the fixture proof becomes sound: *redirected log dir contains a file
within N seconds of launch*. Pair it with ctor hygiene: wrap post-launch
initialization in try/catch that kills the spawned app and disposes automation
before rethrowing - otherwise every failed fixture leaks an invisible running
process (users notice; ours ran unnoticed for 18 minutes).

## 7. Agent anti-hang protocol (the agent itself hangs, not just processes)

Observed failure: `dotnet test ... | Select-Object -Last N` executed inline by
an agent **hung the agent's shell indefinitely even after the test process
exited**. Root cause: orphaned children (testhost, the app under test) inherit
the shell's stdout/stderr pipes; the pipe stays open until every handle holder
dies, and stream-readers wait for EOF, not process exit.

Protocol for any agent touching UI/E2E suites:

1. Never run the suite inline through your tool shell. Execute a detached
   runner script: `Start-Process` with stdout/stderr redirected to FILES,
   poll liveness every ~500 ms, hard-kill the process TREE at a deadline
   (`$proc.Kill($true)`), then print file tails.
2. Subagents/reviewers must not execute UIA suites inside their own tool calls
   either - COM/UIA interactions hang agent wrappers unpredictably. Reviews are
   static plus main-suite runs; empirical UIA evidence comes from exactly one
   designated place (controller or script output committed to artifacts).
3. Kill strays BY NAME after every run (`app name`, `testhost`,
   `<suite>.exe`). An abandoned app survived 18 minutes unnoticed; users see
   "two instances" and lose trust.
4. Prefer MTP-native invocation quirks knowledge: runner rejected `--nologo`;
   MTP 1.9.x has no `--timeout` option - the polling deadline IS the timeout.

## 8. Test-design traps actually caught in review

| Trap | Symptom | Fix |
| --- | --- | --- |
| Sequencing test asserting only final state | Deleting the tested behavior kept it green | Capture INPC/event SEQUENCES, not just end values |
| Existence-check proof on shared dir | Stale debris from hard-killed run passes it vacuously | Pre-clean before launch; assert freshness, not existence |
| Wait loops that "worked" | Investigation showed they never succeeded - later assertions passed for unrelated reasons | After fixing infra, re-verify old passes weren't false-greens |
| Silent-eviction counters (`DropOldest`) | Detection math assumed evictions increment drain totals - unreachable by construction | Trigger episodes off OBSERVABLE state (channel count), not bookkeeping deltas |
| Healthy-killed-run writes nothing | Absence-based proofs fail spuriously | Add explicit startup marker; prove via presence of THAT |
| Culture-dependent UI labels | Item text lookup breaks on other locales | Select by index/order where contract allows, or via app-resource-derived expectations |

## 9. Process lessons for planning agents

- Plans drafted without live API verification WILL contain fabricated APIs and
  wrong property names. Budget for deviation reports; treat them as expected
  deliverables ("verify against reality, adapt minimally, report").
- Empirical discovery is where implementation subagents excel: they root-caused
  mutex thread-affinity (`ReleaseMutex` is thread-tied; `await` resumptions change threads),
  DataGrid virtualization, and FlaUI API drift far better than upfront analysis.
  Delegate discovery; keep rulings and policy centralized with the controller.
- Absolute owner constraints ("never X on my machine") must become STRUCTURE
  the moment they exist, and the enforcement mechanism should be re-audited at
  every hand-off. The cheapest reliable audit: grep gates (input APIs, solution
  membership) recorded in the plan.
- When a GUI-launching mistake happens despite structure, the response order is:
  stop processes -> remove local capability -> encode ruling durably -> only
  then continue the roadmap.

## 10. Pre-flight checklist for FlaUI work

- [ ] Venue decided structurally (out-of-solution / CI job), not by convention.
- [ ] Grep gate green: no `Mouse.` / `Keyboard.` / `VirtualKeyShort` in suite.
- [ ] Env redirects honored; unset means production-identical behavior.
- [ ] Instance/mutex suffix prevents collisions with real instances; suffixed
      runs never activate foreign windows.
- [ ] Launch fixture asserts spawned-PID ownership + a real early redirect
      signal; ctor failure kills the spawned app.
- [ ] All waits bounded; no sleeps-as-synchronization beyond tiny settle delays;
      no inline piped test commands in agent shells.
- [ ] Stray-process cleanup after every run; artifact/log story defined for CI
      failures.
- [ ] Not-tested list written down (UAC flows, destructive kills of real
      processes, restart loops) alongside the inventory.

## 11. Validation trail

Adopted on this repository: harness + 9 E2E behaviors (launch/status strip,
about identity, settings round-trip, export cancel, terminate happy/cancel on
sacrificial victims, priority flow, no-selection validation, second-instance
bounce). Structural CI-only venue verified by solution membership grep; input-
free guarantee verified by grep gate. Functional E2E validation executes on
GitHub runners only - by product-owner ruling after real-input leakage onto
their desktop during the first local attempt.
