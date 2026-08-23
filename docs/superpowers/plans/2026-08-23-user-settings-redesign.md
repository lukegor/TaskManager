# Implementation Plan: User Settings Redesign

**Spec:** `docs/superpowers/specs/2026-08-23-user-settings-redesign-design.md`
**Status:** Proposed
**Date:** 2026-08-23

Each phase is independently shippable and leaves the build green. Verify after every task:
`dotnet build TaskManager.slnx && dotnet test TaskManager.slnx`.

---

## Phase 1 — Store, Service, Consumer Rewiring (fixes live-refresh defect)

### Task 1.1 — `AppSettings` record + validation
- [ ] `TaskManager.Domain/Models/AppSettings.cs`: immutable record
      (`Language`, `ProcessesRefreshFrequency`, `DateTimeFormat`), `Defaults` singleton,
      `AllowedDateTimeFormats`, `TryValidate(settings, out error)`.
- [ ] Validation rules: non-empty Language present in `LanguageDictionary.KeysList`;
      enum defined; format in whitelist.
- [ ] Tests: valid defaults pass; each violated rule produces correct error string.

### Task 1.2 — `ISettingsService` abstraction
- [ ] Replace `IAppSettings` + `ISettingsService` with single `ISettingsService`:
      `AppSettings Current { get; }`, `event Action<AppSettings>? Changed;`,
      `void Update(AppSettings settings);`.
- [ ] Delete `TaskManager.Domain/Abstractions/IAppSettings.cs`, rewrite `ISettingsService.cs`;
      document threading contract (Changed fires on caller thread).
- [ ] Update exporters (`BaseDataExporter` + subclasses), `TimerManager`, `ProcessManager`,
      factories to consume `Current`. Compile fixes only in this task.

### Task 1.3 — `JsonSettingsStore`
- [ ] `TaskManager/Infrastructure/Settings/JsonSettingsStore.cs`: STJ **source-gen** context,
      envelope `{ version, settings }`, `JsonStringEnumConverter`.
- [ ] Path: `%LOCALAPPDATA%\TaskManager\settings.json` (injectable for tests).
- [ ] Atomic write: temp file → `File.Move(overwrite: true)`; retry ×3 on IO share violation.
- [ ] Load: missing → defaults (no file created until first save); corrupt → rename
      `settings.json.corrupt-<timestamp>` + log warning + return defaults;
      per-property fallback for missing fields.
- [ ] Tests per spec §9 (round-trip, corruption, atomicity, unknown fields).

### Task 1.4 — New `SettingsService`
- [ ] Rewrite `TaskManager/Services/SettingsService.cs`: ctor `(JsonSettingsStore, ILogger)`; assign
      `Current` from store load; `Update` = validate → persist → swap → raise `Changed`.
- [ ] No MessageBox, no `App.Restart`, no reflection over designer defaults.
- [ ] DI: remove dual registration; register `JsonSettingsStore` (singleton) and
      `ISettingsService → SettingsService` (singleton).
- [ ] Tests: update persists then raises; invalid input persists nothing, raises nothing;
      corrupt store → defaults + log (no subclassing needed anymore).

### Task 1.5 — Wire consumers (fixes A3)
- [ ] `ProcessManager`: subscribe `_settings.Changed += OnSettingsChanged`; handler maps
      frequency → `_timer.UpdatePolling(seconds)`. Remove dead `Default_PropertyChanged`.
- [ ] Unsubscribe not required (singletons share lifetime).
- [ ] Test: raising `Changed` with new snapshot updates timer interval.

### Task 1.6 — Legacy migration
- [ ] In `JsonSettingsStore.Load`: if JSON absent, attempt read of legacy `user.config`
      (`ApplicationSettingsBase`) values; best-effort import with per-field try-parse; write JSON.
- [ ] Test: seeded legacy values imported; garbage legacy value falls back per-field.

**Phase gate:** manual check — change refresh frequency at runtime, observe polling interval change without restart.

---

## Phase 2 — Dialog Rewrite, Defaults Fix, Deletions

### Task 2.1 — ViewModel on record copies
- [ ] `SettingsWindowViewModel`: editable properties initialized from `_settings.Current`;
      Save builds candidate → `_errorHandler.Guard(() => _settings.Update(candidate))`;
      move date-format whitelist usage to `AppSettings.AllowedDateTimeFormats`.
- [ ] Delete `EditableSettings.cs` and `SettingsMapper.cs`; update tests referencing them.

### Task 2.2 — Restore Defaults via pipeline (fixes A4)
- [ ] `RestoreDefaultsCommand` → `Update(AppSettings.Defaults)` through same guard.
- [ ] Test: reset raises `Changed`, persists defaults, subsequent Save cannot resurrect old values.

### Task 2.3 — Restart decision out of service (fixes A6)
- [ ] Remove restart logic from anywhere in service layer. `MainWindowViewModel.OpenSettings`
      captures `before = Current`, shows dialog, compares `Current.Language`; if changed → `App.Restart()`.
- [ ] Alternatively resolve a small `ISettingsDialogService` from DI that owns this sequence —
      prefer inline in VM unless it grows.

### Task 2.4 — DI & dialog resolution consistency (fixes A9)
- [ ] Register `SettingsWindowViewModel` transient + window factory consistent with
      `DataExportWindow` pattern; remove `new SettingsWindow()` from `MainWindowViewModel`.

### Task 2.5 — Delete legacy persistence
- [ ] Delete `Properties/Settings.Designer.cs`, `Properties/Settings.settings`(if present),
      legacy fallback/recovery code paths, now-dead localized strings if any.
- [ ] Keep migration reader only (it must not depend on the Designer type — read config XML directly
      or retain minimal private shim; choose during implementation, document choice).
- [ ] Full test-suite run; grep for `Settings.Default` must return zero hits outside migration shim.

---

## Phase 3 — Optional: Runtime Language Switching (separate spec if pursued)

- [ ] Replace `{x:Static resx:Strings.*}` usages with DynamicResource keys backed by a merged
      ResourceDictionary regenerated per culture; re-set thread cultures on change; drop restart path.
- [ ] Estimate first: ~40 XAML touch points across views. Only start with explicit user approval.

---

## Rollback / Safety

- Phase 1 ships behind nothing — behavior-compatible except settings file location (migration covers it).
- If JSON store fails catastrophically in the field, quarantined files + logs pinpoint cause; app still runs
  on defaults (startup degradation already proven by existing recovery tests, now simplified).
