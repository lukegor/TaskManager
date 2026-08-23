# Design Specification: User Settings Redesign — Immutable Snapshots over Typed JSON

**Status:** Proposed design
**Date:** 2026-08-23

## 1. Overview & Goals

Audit verdict: the current settings system is **not optimal**. It is built on a legacy persistence
mechanism that forces stringly-typed storage, compensating recovery machinery, and a change-notification
contract that is silently broken. Three functional defects exist today:

1. **Refresh-frequency changes never apply at runtime.**
   `ProcessManager.Default_PropertyChanged` (TaskManager.Domain/Services/ProcessManager.cs:31) is defined to
   call `_timer.UpdatePolling(...)` but is **never subscribed** — no code wires it to the settings object.
   Saving a new frequency persists it and notifies nobody; `TimerManager` keeps the old interval until restart.
2. **Restore Defaults is a no-op with side effects.**
   `SettingsService.RestoreDefaults` calls only `Settings.Default.Reset()` (TaskManager/Services/SettingsService.cs:94).
   The service's own observable properties are never refreshed, `PropertyChanged` never reaches consumers,
   and the open dialog still holds stale values — pressing *Save* afterwards re-persists the old settings.
3. **Language switch is coupled to process restart inside the persistence service.**
   `SaveSettings` detects a language delta and calls static `App.Restart()`, burying UI lifecycle control in
   the data layer.

Goals:

- One typed, validated, versioned settings model persisted atomically as JSON.
- Change propagation that cannot be silently unwired (explicit event, consumers subscribe or don't read).
- Delete, not patch: remove the corrupt-settings recovery subsystem, the legacy Designer store, the
  edit-buffer DTO + mapper pair.
- Restore Defaults becomes the same pipeline as Save (persist + notify), fixing defect 2 by construction.
- Keep "restart on language change" for now (resx/x:Static localization makes runtime switching a separate
  project) but move the decision out of the service into app startup composition (see §7).

## 2. Scope

**In scope**

- New immutable `AppSettings` record (Domain) replacing mutable `IAppSettings` property bag.
- `ISettingsService` exposing `Current`, `Changed`, `Update`, `Defaults`.
- `JsonSettingsStore`: System.Text.Json source-gen, atomic write, schema `version` field, corrupt-file
  quarantine, one-time migration from legacy `user.config`.
- Rewiring `ProcessManager` / `TimerManager` to the explicit `Changed` event (fixes defect 1).
- Rewrite of `SettingsWindowViewModel` to bind against a record copy; deletion of `EditableSettings`,
  `SettingsMapper`; `RestoreDefaults` → `Update(Defaults)` (fixes defect 2).
- Moving the language-restart decision out of `SettingsService` (fixes defect 3).
- DI cleanup: single registration; `SettingsWindow` resolved consistently with other dialogs.
- Tests: round-trip, corruption quarantine, atomicity, propagation, defaults, legacy migration.

**Out of scope**

- Runtime language switching without restart (Phase 3 option, see §8 — requires localizer redesign).
- New user-facing settings (column layout, theme, etc.) — architecture must make them trivial, not ship them.
- Microsoft.Extensions.Options/IOptionsMonitor adoption (evaluated and rejected, §5 Option C).
- Roaming profiles, cloud sync, per-user multi-profile support.

## 3. Current Architecture (as audited)

```
Properties/Settings.Designer.cs      ApplicationSettingsBase singleton (legacy, static, string-typed)
        ▲ read/write                          LanguageVersion : string ("English")
        │                                     RefreshFrequency : string "1" → int.Parse → enum cast
TaskManager.Services.SettingsService  ObservableObject; IAppSettings + ISettingsService
        │                                     corrupt-load fallback chain, App.Restart() on save
        ├── IAppSettings ──► ProcessManager, TimerManager, exporters (read props; INPC implicit)
        └── ISettingsService ──► SettingsWindowViewModel
                    └── EditableSettings (Domain, ObservableObject) ◄── SettingsMapper
```

### 3.1 Findings

| # | Finding | Severity | Evidence |
|---|---------|----------|----------|
| A1 | Legacy WinForms-era store: static singleton, Designer file, opaque `user.config` XML, no versioning/migration | High | `Settings.Designer.cs`, `Settings.Default.Save()` |
| A2 | Enum stored as `"1"` string, parsed at load; any garbage throws and requires the entire corrupt-recovery fallback chain | High | `SettingsService.LoadSettings` / `LoadDefaultSettings` |
| A3 | Missing event subscription → refresh-frequency changes inert until restart (dead `Default_PropertyChanged`) | Critical (functional bug) | `ProcessManager.cs:31` never wired |
| A4 | `RestoreDefaults` resets raw store only: no service refresh, no notification, dialog buffer goes stale, next Save resurrects old values | High (functional bug) | `SettingsService.cs:94` |
| A5 | Domain contains UI edit-buffer DTO (`EditableSettings : ObservableObject`) + mapper; write API accepts the DTO | Medium (layering) | `Domain/Models/EditableSettings.cs`, `SettingsMapper.cs` |
| A6 | Persistence service calls static `App.Restart()` — UI lifecycle buried in data layer | Medium (layering) | `SettingsService.cs:90` |
| A7 | Consumers depend on implicit `INotifyPropertyChanged` not declared by `IAppSettings`; nothing enforces wiring | Medium (hidden contract) | enabled defect A3 |
| A8 | Testability friction: tests must subclass the service to fake the uninjectable static store; assertions reflect over `Settings.Default.Properties[...]` | Medium | `SettingsServiceTests.cs:17-34` |
| A9 | DI inconsistency: `MainWindowViewModel.OpenSettings` news up `SettingsWindow` directly while `DataExportWindow` resolves via DI | Low | `MainWindowViewModel.cs:130-137` |
| A10 | No validation boundary: `DateTimeFormats` whitelist hardcoded in the VM; invalid persisted values surface as load-time exceptions instead of being rejected at save/load | Low | `SettingsWindowViewModel.cs:21-24` |

## 4. Proposed Architecture

Core idea: **one immutable snapshot + one tiny reactive service + one JSON file.**

```
AppSettings                      immutable record (Domain): typed values + validation factory
ISettingsService                 Current / Changed / Update / Defaults   (Domain abstraction)
        ▲
JsonSettingsStore                typed JSON, source-gen, atomic write, quarantine, migration (app host)
SettingsService                  holds Current; Update = validate → persist → swap → raise Changed
        │ Changed event (explicit)
ProcessManager / TimerManager    subscribe once in ctor; recompute timer interval from snapshot
SettingsWindowViewModel          binds to copy of record; Save → Update(copy); Reset → Update(Defaults)
App (composition root)           decides restart-on-language-change by comparing snapshots
```

### 4.1 Domain model

```csharp
public sealed record AppSettings
{
    public required string Language { get; init; }              // e.g. "English"
    public required RefreshFrequencyType ProcessesRefreshFrequency { get; init; }
    public required string DateTimeFormat { get; init; }        // whitelisted format string

    public static AppSettings Defaults { get; } = new()
    {
        Language = "English",
        ProcessesRefreshFrequency = RefreshFrequencyType.Low,
        DateTimeFormat = "yyyy_MM_dd--HH_mm_ss",
    };

    // single validation gate used by deserializer callback AND Update()
    public static bool TryValidate(AppSettings s, [NotNullWhen(false)] out string? error);
}
```

- Immutable records give value equality for free — the restart check becomes `old.Language != new.Language`.
- No `INotifyPropertyChanged` on settings data. Reactivity lives exclusively in the service's `Changed`
  event. Nothing can half-subscribe to individual properties again.
- Validation is one function (`TryValidate`), called on every entry point. On **load**, invalid persisted
  input degrades to defaults + log (data, no escaping exception). On **update** (explicit user action),
  rejection surfaces to the existing Tier 2 error handler so the user sees why Save failed.

### 4.2 Abstraction

```csharp
public interface ISettingsService
{
    AppSettings Current { get; }
    event Action<AppSettings>? Changed;
    void Update(AppSettings settings);   // validate → persist → swap → raise Changed (in that order)
}
```

Read/write segregation via two interfaces is dropped: with an immutable snapshot there is no partial-write
risk justifying the split, and the second interface added zero enforcement (defect A7). Consumers that only
read take `Current` at use time; long-lived consumers subscribe to `Changed`.

Threading contract: `Changed` fires synchronously on the thread that called `Update` (the UI thread in
practice). Handlers that touch UI-bound state marshal through `IDispatcherService`, exactly as
`ProcessManager.LoadProcesses` already does. Documented on the interface.

### 4.3 Persistence — `JsonSettingsStore`

- File: `%LOCALAPPDATA%\TaskManager\settings.json`.
- System.Text.Json **source generation**, `WriteIndented`, `JsonStringEnumConverter` — enums persist as
  `"Low"`, not `1`; readable, diffable, forward-compatible.
- Schema envelope: `{ "version": 1, "settings": { ... } }`. Unknown fields ignored (`default`), missing
  fields fall back to `Defaults` per-property during materialization. Adding setting #4 later needs no
  migration code.
- **Atomic write:** serialize to `settings.json.tmp`, then `File.Move(tmp, target, overwrite: true)`. A crash
  mid-write can never truncate the real file.
- **Corruption handling replaces the recovery subsystem:** one `try/catch` around load. Corrupt/unparsable
  → rename file to `settings.json.corrupt-<timestamp>`, return `Defaults`, log warning. No MessageBox at
  construction time, no reflection over designer defaults, no nested try/catch (deletes ~40 lines).
- **One-time legacy migration:** if JSON absent but legacy `user.config` has values, import them (best-effort,
  per-field try-parse with defaults on garbage) and write the JSON file. Subsequent runs ignore the legacy
  store entirely.

### 4.4 Application-layer service

```csharp
internal sealed class SettingsService : ISettingsService
{
    public AppSettings Current { get; private set; }
    public event Action<AppSettings>? Changed;

    public void Update(AppSettings settings)
    {
        if (!AppSettings.TryValidate(settings, out var error))
            throw new SettingsValidationException(error);   // caught by IErrorHandler tier like any command failure

        _store.Save(settings);           // atomic; throws on IO failure → guarded command path
        Current = settings;
        Changed?.Invoke(settings);
    }
}
```

Constructor takes `(JsonSettingsStore store, ILogger<SettingsService> logger)`. Load happens in the store;
the constructor assigns `Current` and logs if defaults were substituted. No message boxes from services —
startup degradation is silent-with-log, consistent with the three-tier error design already adopted.

### 4.5 Dialog flow

`SettingsWindowViewModel` binds to plain editable properties (Language, frequency, date-format) initialized
from `service.Current`. Save constructs a candidate record and calls `Update(candidate)`. There is no
`EditableSettings`, no mapper — the record *is* the transfer type, and the VM owns transient editing state.

The date-format whitelist moves from the VM into `AppSettings` (`AllowedDateTimeFormats`), so validation and
options come from one place and tests can cover both.

### 4.6 Consumer rewiring

```csharp
// ProcessManager ctor
_settings.Changed += OnSettingsChanged;

private void OnSettingsChanged(AppSettings s) =>
    _timer.UpdatePolling(RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[s.ProcessesRefreshFrequency]);
```

This also fixes defect A3 permanently: the event is declared on the interface, so wiring it is enforced by
the compiler rather than by convention — the previous failure mode (a handler defined but never subscribed)
can no longer compile. Exporters keep taking the snapshot they
need per export call — unchanged usage pattern, now reading `Current.DateTimeFormat`.

## 5. Alternatives Considered

**Option A — Patch the existing system (keep ApplicationSettingsBase):**
wire the missing event, fix `RestoreDefaults` to reload + notify. Least effort, but retains the static
untestable store, stringly-typed persistence, Designer file, and the recovery machinery whose only reason to
exist is A2's fragility. Rejected: fixes symptoms, preserves the causes.

**Option B — Immutable snapshot + JSON store (chosen):**
~80 lines of explicit, fully testable infrastructure; deletes more code than it adds; fixes all three
functional defects structurally rather than procedurally. Chosen.

**Option C — Microsoft.Extensions.Options + IConfiguration + IOptionsMonitor:**
idiomatic for services/ASP.NET; brings reload tokens, binder, validation plumbing. For three user-writable
settings in a WPF app it adds packages, FileSystemWatcher semantics designed for operator-edited config
(user edits happen through our own dialog), and indirection (`IOptionsMonitor<>` everywhere) that obscures
more than it organizes. Rejected on YAGNI; revisit if settings count grows past ~a dozen sections.

## 6. Error Handling Alignment

Consistent with the approved three-tier design (2026-08-23 exception-handling spec):

- Tier 1: validation failures are data (`TryValidate`); corrupt files are quarantined as data events.
- Tier 2: `Save` failures surface through the existing `IErrorHandler.Guard` in the VM command.
- Tier 3: untouched; startup load failure degrades to defaults + log, never escapes the constructor
  (regression test retained, rewritten against injectable store).

## 7. Language & Restart Policy

v1 behavior preserved: changing language still requires restart (resx + `x:Static` bindings are baked at
XAML compile time). But the *decision* moves to the composition root:

```csharp
// App, after dialog closes
var before = _settings.Current;
dialog.ShowDialog();
if (_settings.Current != before && before.Language != _settings.Current.Language) Restart();
```

`SettingsService` loses its `App.Restart()` call entirely (defect A6). Phase 3 (§8) removes the restart by
making resources dynamic.

## 8. Phasing (summary — detail in plan)

1. **Phase 1 — Store + service + consumer rewiring.** New model/abstraction/store/service; wire
   `ProcessManager`; legacy migration. Ships value immediately: refresh frequency works live (A3 fixed).
2. **Phase 2 — Dialog rewrite + deletions.** VM on record copies; `RestoreDefaults` = `Update(Defaults)`
   (A4 fixed); restart decision moved (A6 fixed); delete `EditableSettings`, `SettingsMapper`,
   `Settings.Designer.cs`, recovery machinery; DI consistency (A9).
3. **Phase 3 — Optional: runtime language switching.** Replace `x:Static` with DynamicResource-backed
   localization; drop restart path. Separate spec if pursued.

## 9. Testing Strategy

- `JsonSettingsStore` (temp-dir injected path): round-trip; unknown/missing fields → defaults per-property;
  corrupt JSON → quarantine file created + defaults returned; write interrupted (simulate by pre-creating tmp)
  leaves original intact; enum round-trips as string; version bump tolerated.
- `SettingsService`: `Update` persists then raises `Changed` with same instance; validation rejection raises
  nothing and persists nothing; constructor with corrupt store yields defaults + log (replaces subclassing
  hack — store is injected).
- Migration: seeded legacy `user.config` imported; garbage legacy values fall back per-field.
- `ProcessManager`: raising `Changed` updates timer interval mapping (NSubstitute `TimerManager` or fake).
- ViewModel (StaFact): save calls `Update` with edited values; reset calls `Update(Defaults)`; failed update
  routes through `IErrorHandler`.

## 10. Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Users lose current settings on upgrade | One-time legacy migration reads `user.config` first run |
| File locked by AV/indexer during write | Atomic rename retry ×3 short backoff; failure surfaces via Tier 2 guard |
| Future settings bloat the flat record | Record stays flat until ~8–10 settings, then group into nested records within same envelope (version field already supports it) |
| `Changed` handler exceptions break save flow | Handlers wrap their own work per existing error tiers; `Update` raises after successful persist, so state is consistent even if a handler fails |
