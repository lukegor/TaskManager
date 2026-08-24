# Project Structure Consolidation — Design

**Date:** 2026-08-24
**Status:** Approved
**Scope:** Solution layout, project boundaries, code ownership, naming, csproj hygiene, documentation

## Problem

The solution has 5 projects for ~4k LOC of application code. Two of them exist mostly by historical accident:

- **`TaskManager.Shared`** contains only localization resources (Strings.resx + generated Designer). Its content serves exactly one real consumer: the app.
- **`TaskManager.Utility`** is a grab-bag mixing domain vocabulary (enums), serialization helpers, settings support, and WPF visual-tree code. The last item forces `UseWPF` on the entire library and drags a UI framework into Domain's dependency graph transitively.

Additional hygiene issues:

- Namespace `TaskManager.Utility.Utility`
- App folder `TaskManager/Utility/Converters` collides in name with the `TaskManager.Utility` assembly
- `Services/Data Export` folder name with a space forces `Data_Export` namespaces
- Duplicate/conflicting `<OutputPath>` in `TaskManager.csproj`; duplicate `<Using Include="Xunit">` in tests
- No SDK version pinned in `global.json`
- README is an empty shell
- Integration tests (which touch live system state) run mixed with unit tests

## Goal

A solution shape an experienced team would pick for this codebase: fewest assemblies that still enforce the architecture, every piece of code owned by exactly one project, no UI dependencies below the exe, zero namespace friction.

## Target Structure

```
src/
  TaskManager/                     WPF exe (UI, ViewModels, service impls, resources)
  TaskManager.Domain/              UI-free logic library (models, engines, P/Invoke)
tests/
  TaskManager.UnitTests/
  TaskManager.IntegrationTests/
TaskManager.slnx                   updated project paths
```

Dependency graph after the change:

```
TaskManager.IntegrationTests → TaskManager.Domain (+ app where needed)
TaskManager.UnitTests        → TaskManager.Domain + TaskManager (exe; UI behavior/control tests)
TaskManager (exe)            → TaskManager.Domain
TaskManager.Domain           → BCL + NtApiDotNet + ClosedXML only
```

No project except the exe references WPF. `Shared` and `Utility` are deleted.

## Code Ownership Moves

| Source | Destination | Rationale |
|---|---|---|
| `Shared/Resources/Languages/*` (Strings.resx, Strings.pl.resx, Strings.Designer.cs) | `src/TaskManager/Resources/Localization/`, namespace `TaskManager.Resources.Languages` | App is sole consumer |
| `Utility`: `ArchitectureType`, `DataType`, `OperationType`, `RefreshFrequencyType`, `ExportationType`, `EnumHelper`, `EnumExtensions`, `PriorityTypeHelper` | `TaskManager.Domain/Primitives/` | Domain vocabulary used by Domain models/services |
| `Utility`: `IgnoreSerialization` | `TaskManager.Domain` (beside its consumers) | Used only by Domain Process/exporters |
| `Utility`: `LanguageDictionary` | `src/TaskManager/Infrastructure/Settings/` | Settings/app concern |
| `Utility`: `VisualTreeUtilityHelper` | `src/TaskManager/UI/` | WPF-only; only consumed by BetterDataGrid + GridColumnVisibility |
| `Utility`: `Preconditions` | **Deleted** | Duplicates BCL (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIf*`); single call site |

All moved types get namespaces matching their new home. No `TaskManager.Utility` namespace survives.

## Naming & Hygiene Fixes

1. Rename `Domain/Services/Data Export` → `DataExport`; drop underscored namespaces.
2. Move app's `TaskManager/Utility/Converters/` → `TaskManager/UI/Converters/`.
3. Standardize test namespaces to `TaskManager.UnitTests.<Area>` and `TaskManager.IntegrationTests.<Area>`; align test folders with them.
4. Fix duplicate `<OutputPath>` in `TaskManager.csproj` (keep one canonical definition, ideally in `Directory.Build.props`).
5. Merge duplicated `<Using>` ItemGroups in test csproj.
6. Pin SDK in `global.json` with `rollForward: latestFeature`.
7. Slim or remove `ViewModelBase` if it adds nothing over CommunityToolkit `ObservableObject` beyond one helper; keep only what earns its place.
8. Rewrite README: what the app is, architecture sketch, build/run/test commands.

## Integration Test Separation

Integration tests exercise live process management (real PIDs, system state). They get their own csproj so:

- Default `dotnet test` runs the fast, hermetic unit suite.
- Integration suite runs explicitly: `dotnet test tests/TaskManager.IntegrationTests`.

The single shared helper (`TestSupport/GridTestHost.cs`) stays with unit tests; integration tests link it via `<Compile Include>` if needed rather than adding another shared project.

## Migration Order

1. Create `src/` / `tests/` folders; move projects with `git mv`; update `TaskManager.slnx`.
2. Dissolve `Shared`: move resx + Designer into app, update namespaces and consumers.
3. Dissolve `Utility`: move each file per ownership table; delete `Preconditions`; update all usings.
4. Apply naming fixes (folders, namespaces, converters move).
5. Split test project into UnitTests + IntegrationTests; fix csproj duplication.
6. csproj/global.json/README polish.
7. Verify: clean `dotnet build`; full unit suite green; integration suite runs on demand; `git ls-files` shows no binaries; grep confirms no surviving `TaskManager.Utility` / `TaskManager.Shared` references.

## Non-Goals

- No behavior changes in Domain engines, services, or ViewModels beyond mechanical namespace/type moves.
- No new features, no dependency upgrades beyond what consolidation requires.
- No CI pipeline work (out of scope unless trivially touched by test-project split).
