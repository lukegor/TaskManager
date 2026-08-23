# Design Specification: Exception Handling Optimization — Three-Tier Defense

**Status:** Approved design
**Date:** 2026-08-23

## 1. Overview & Goals

Establish a coherent, layered error-handling strategy for Task Manager. Today the app has five narrow,
well-motivated catch blocks but no global safety net, no logging infrastructure, an unguarded process-kill
path, and a settings-recovery fallback that itself throws (`SettingsService.LoadDefaultSettings` calls
`Convert.ToInt32(nameof(...))`, which throws `FormatException` unconditionally). Any unexpected exception on
any code path currently terminates the process silently.

Goals:

- Handle every error at the lowest tier that understands it; higher tiers are safety nets, not the strategy.
- Make expected OS failures (stale PIDs, access denied, locked export files) data instead of exceptions.
- Guarantee no silent failure: every handled or swallowed exception is logged to a persistent file sink.
- Fix the identified crash vectors: settings fallback, `async void` polling callback, unguarded
  `TerminateProcesses`, unguarded startup load, exporter IO.

Runtime constraint that shapes this design: "continue after unexpected error" is only possible for UI-thread
(dispatcher) exceptions. An unhandled exception on a background threadpool thread is always fatal in modern
.NET. The design therefore ensures background paths never let exceptions escape, and treats
`AppDomain.UnhandledException` as a fatal crash reporter rather than a recovery mechanism.

## 2. Scope

**In scope**

- Logging infrastructure: `Microsoft.Extensions.Logging.Abstractions` + a small in-repo rolling file sink.
- Three handling tiers: domain result summaries, command guard, global handlers.
- Targeted fixes intersecting error flow (settings fallback bug, polling guard, kill hardening,
  guarded startup load, dead-code removal).
- Localized strings for all new user-facing messages.
- Fault-injection tests for every new handling path.

**Out of scope**

- Custom exception hierarchy or general Result monad.
- Full async refactor of startup (`LoadProcesses().GetAwaiter().GetResult()` remains but becomes guarded).
- Retry policies, telemetry/remote reporting, structured logging frameworks.
- Refactoring of code paths with no error-flow impact.

## 3. Architecture

```
Tier 3  Global handlers (App)        ← last resort, safety net
Tier 2  IErrorHandler (ViewModels)   ← unexpected errors in commands
Tier 1  Result summaries (Domain)    ← expected OS failures as data
```

Layering rules:

- Domain knows results and `ILogger<T>` only. No WPF types, no dialogs.
- UI never inspects exception types; it consumes result summaries and delegates surprises to Tier 2.
- Tier 3 handlers are thin and delegate their logic to testable components.

### 3.1 New components

| Component | Project | Responsibility |
|---|---|---|
| `FileLoggerProvider` | TaskManager (`Infrastructure/Logging/`) | Rolling file sink: `%LOCALAPPDATA%\TaskManager\logs\tm-YYYYMMDD.log`; daily roll, 7-day retention, thread-safe via lock, minimum level Debug (so per-process enumeration diagnostics persist). ~80 LOC, only dependency is `Microsoft.Extensions.Logging.Abstractions`. |
| `IErrorHandler` / `UiErrorHandler` | TaskManager | `Handle(Exception ex, string context)` → logs Error with category + context, shows one localized dialog via existing `IMessageService`. Reentrancy-proof: if its own logic faults, the exception falls through silently to Tier 3 instead of looping dialogs. |
| `ProcessOpSummary` (+ `ProcessOpFailureReason`) | TaskManager.Domain | Record with `SucceededPids` and `Failures: [(pid, reason)]`. Reasons: `ProcessExited`, `AccessDenied`, `Unknown`. Returned by `SetPriority` and `TerminateProcesses`. |
| `ExportResult` (+ `ExportFailureReason`) | TaskManager.Domain | `Success(path)` / `Failure(reason)` returned by all five exporters. Reasons include at least `IoError`, `AccessDenied`, `InvalidPath`. |

### 3.2 Wiring

- Composition root registers logging: `services.AddLogging(b => b.AddProvider(new FileLoggerProvider(...)))`.
- Domain classes receive `ILogger<T>` via constructor injection.
- ViewModels receive `IErrorHandler`; each fallible command adopts it in one line by wrapping its body in
  `_errorHandler.Guard(...)` / `GuardAsync(...)`.
- New localized strings go to `TaskManager.Shared` resx.

### 3.3 Exception taxonomy

None. No custom exception classes. Expected failures become result data; anything still throwing is genuinely
unexpected and handled uniformly by Tier 2/3 through one message path.

## 4. Behavior Contracts

### 4.1 Tier 1 — Domain (expected failures as data)

- `SetPriority` / `TerminateProcesses`: per-PID loop maps `ArgumentException` → `ProcessExited`,
  `Win32Exception` (ERROR_ACCESS_DENIED) → `AccessDenied`, `InvalidOperationException` → `ProcessExited`;
  anything else is logged at Warning and recorded as `Unknown`. The batch always continues. Methods never
  throw for OS-level rejections; they return a summary.
- Exporters: `IOException`, `UnauthorizedAccessException`, and invalid-path `ArgumentException` produce
  `ExportResult.Failure(reason)` logged at Warning. Unexpected exceptions still propagate (Tier 2).
- `GetProcesses()`: per-process swallow stays; the commented-out log line becomes a real `LogDebug` carrying
  the exception detail.

### 4.2 Tier 2 — Command guard

- `GuardAsync(Func<Task>)` / `Guard(Action)` catch-all → `UiErrorHandler.Handle(ex, context)`:
  log + localized dialog; the command remains usable afterwards.
- Adopted by: terminate command, priority command, export command, settings save/restart path.

### 4.3 Tier 3 — Global handlers (App)

- `DispatcherUnhandledException`: log Error, show dialog with **Continue / Exit** choice;
  `e.Handled = true` only when the user chooses Continue.
- `AppDomain.UnhandledException`: write Fatal crash report (full stack + app version), flush, best-effort
  apology dialog. The process dies as the runtime demands.
- `TaskScheduler.UnobservedTaskException`: log Warning, mark observed.
- Registrations live in `App.OnStartup`; handler bodies delegate to `UiErrorHandler`/logger so they are testable.

### 4.4 Targeted fixes

1. **Settings fallback bug:** `LoadDefaultSettings` reads real typed defaults from `Settings.Default`
   properties instead of converting property-name strings. The fallback path is itself wrapped and logged —
   corrupt settings now degrade to defaults with a dialog, never a startup crash.
2. **`OnProcessPolling` async void:** entire body wrapped in try/catch-all logging at Warning; the existing
   `TaskCanceledException` shutdown handling is preserved inside. A timer tick can no longer kill the process.
3. **Guarded startup load:** `LoadProcesses().GetAwaiter().GetResult()` wrapped — failure logs, shows a
   dialog, and starts with an empty process list instead of aborting startup.
4. **Dead code:** delete the empty `async void App_Close` handler in `MainWindowViewModel`.
5. **VM translation:** terminate/priority commands consume summaries and report partial outcomes, e.g.
   "2 terminated, 1 failed (access denied)", via localized strings.

### 4.5 Example data flows

```
Kill button → Guard(TerminateProcesses)
  ├─ stale PID      → summary{failed: ProcessExited} → info message, no throw
  ├─ access denied  → summary{failed: AccessDenied}  → warning message
  └─ bug in VM code → Tier 2 catches → log+dialog → app continues

Timer tick fault → caught in OnProcessPolling → LogWarning only (no dialog spam)

Truly stray UI-thread exception → Tier 3 dispatcher handler → Continue/Exit
Background threadpool fault     → fatal by CLR design → Tier 3 logs crash report
Corrupt settings file           → defaults loaded + dialog (fallback cannot throw)
```

## 5. Testing Strategy

Fault injection is the point of this work; every new handling path gets a deterministic test.

- **Domain summaries:** extend the real-process pattern from `SetPriority_StalePid_DoesNotAbortRemainingUpdates`
  — mixed live/dead/denied PIDs → assert summary contents and batch continuation. `TerminateProcesses` gains
  the contract test it currently lacks.
- **Exporters:** expected failures via invalid paths / locked files where deterministic; VM-level export
  failure tested by making the mocked exporter factory return `Failure` or throw — assert message shown,
  no crash.
- **`UiErrorHandler`:** NSubstitute `IMessageService` + in-memory logger → asserts log-with-context plus
  exactly one dialog call; verifies an internal fault of the handler itself does not rethrow.
- **`FileLoggerProvider`:** writes a line to a temp directory, daily-roll filename logic, retention deletion.
- **Settings fallback:** corrupt settings source → defaults loaded, no throw (regression test for the crash bug).
- **Polling guard:** extracted guarded method tested directly with a throwing dependency → logged, not crashed.
- Global handler *registrations* stay thin and are covered by manual smoke checks, not unit tests.

Conventions unchanged: xUnit v3, `[WpfFact]` only where a dispatcher is genuinely required, NSubstitute for
abstractions.

## 6. Rollout Order

Each step leaves the app shippable:

1. Logging infrastructure (`FileLoggerProvider` + DI wiring) — foundation, zero behavior change.
2. Tier 3 global handlers — immediate crash protection while other work proceeds.
3. Settings-fallback fix + regression test.
4. Domain result summaries (`ProcessOpSummary`, `ExportResult`) + contract tests.
5. Tier 2 `IErrorHandler` + VM adoptions + localized strings.
6. Polling/startup guards + dead-code removal.

## 7. Explicit Non-Goals

Result monad; custom exception hierarchy; full async cleanup of startup; retry policies; telemetry;
third-party logging frameworks beyond `Logging.Abstractions`.
