# Design Specification: DataGrid Controls Composition Redesign

**Status:** Approved design — supersedes `2026-08-23-customized-datagrid-controls-design.md`
**Date:** 2026-08-23

## 1. Overview & Goals

Redesign the custom WPF DataGrid controls in Task Manager around composition instead of the current
`BaseBetterDataGrid` → `BetterDataGrid` → `ProcessDataGrid` inheritance chain, and establish a behavior-level
UI test suite so correctness is verifiable.

Goals:

- Replace rigid subclass-per-domain with one configurable control plus composable behaviors and formatters.
- Make selection single-sourced, right-click Windows-standard, and context menus correctly placed.
- Remove all domain knowledge (`ProcessItem`, process commands) from the generic control.
- Cover every behavioral contract in this document with deterministic STA tests. Test behavior, not implementation.

## 2. Scope

**In scope**

- Redesign of grid controls, behaviors, formatters, and their wiring in `MainWindow`.
- Selection unification across checkbox, row highlight, and model state.
- STA behavior test suite for the grid (`Xunit.StaFact`, already referenced).

**Out of scope**

- New grid features (sorting UX, filtering, column-layout persistence).
- Broad `MainWindowViewModel` / `ProcessManager` test debt (their constructors start polling and load real
  processes; needs its own seam work).
- Selection survival across polling refreshes (unchanged from today).

## 3. Architecture & Components

### 3.1 Component map (after refactor)

```
TaskManager/UI/
├── Controls/
│   └── BetterDataGrid.cs        ← single concrete control (Base* and Process* deleted)
├── Behaviors/
│   └── GridColumnVisibility     ← header menu + per-column locking (CheckBoxClickBehavior deleted)
└── Formatters/
    ├── IRowTextFormatter.cs     ← interface: string Format(object item)
    └── ProcessRowFormatter.cs   ← app-layer impl: tab-delimited via Process.ToDelimitedString('\t')

TaskManager.Domain/Models/ProcessItem.cs  ← gains INotifyPropertyChanged
```

Dependency direction is strictly one-way: `View → Behaviors/Formatters → Control`. Domain models know nothing about UI.

### 3.2 BetterDataGrid responsibilities (generic only)

1. **Right-click routing** — hit-test rows vs. column headers, apply Windows-standard selection semantics,
   open the appropriate context menu at the cursor (`PlacementTarget = this`,
   `PlacementMode.MousePoint`).
2. **Row context menu hosting** — the menu is declared in XAML by the view and assigned to a new
   `RowContextMenu` DP of type `ContextMenu`. Menu items bind to VM commands directly (a ContextMenu inherits
   DataContext from its placement target); no `Tag` plumbing, no per-domain command DPs on the control.
3. **Copy infrastructure** — public static `RoutedUICommand CopyRowsCommand` plus a `CommandBinding` on the
   grid instance (same pattern as TextBox Cut/Copy/Paste). Formatting delegates to an injected
   `IRowTextFormatter` exposed as a plain CLR property settable from XAML; default implementation returns
   `item?.ToString() ?? string.Empty`.

### 3.3 Deleted components

- `BaseBetterDataGrid.cs`, `ProcessDataGrid.cs`, `CheckBoxClickBehavior.cs`
- `MonitorCommand`, `SetPriorityCommand`, `TerminateCommand` dependency properties (Monitor becomes a plain
  `MenuItem` bound to the VM command like Terminate/SetPriority)
- VM `BetterDataGrid_SelectionChangedCommand`, its handler, debug diagnostics, and dead code
  (`OnSelectedItemsPropertyChanged`, no-op `OnSelectionChanged`, unused `MonitorMenuItemClick` event)

## 4. Behavior Contracts

### 4.1 Unified selection (one state, one path)

- `ProcessItem.IsSelected` raises `PropertyChanged`; it is the single source of truth.
- The grid sets an implicit `ItemContainerStyle`: `DataGridRow.IsSelected = {Binding IsSelected, Mode=TwoWay}`.
  Native click/Ctrl/Shift selection writes through to the model; model changes drive row highlights.
- Checkbox binds `IsChecked="{Binding IsSelected, Mode=TwoWay}"` against its own row `DataContext`
  (fixes today's ancestor-binding bug). No behavior class involved.
- Net contract: ticking a box selects the row; Ctrl-clicking rows checks boxes; VM commands keep reading
  `_processManager.Processes.Where(p => p.IsSelected)` unchanged.

### 4.2 Right-click semantics (Windows-standard)

| Scenario | Result |
|---|---|
| Right-click row not in current selection | Collapse selection to that row, open row menu |
| Right-click row already in multi-selection | Selection untouched, open row menu |
| Right-click column headers | Open visibility menu at cursor |
| Right-click empty area / scrollbar / chrome | No-op |

Row menus always operate on the unified selection, never on hidden per-item tags.

### 4.3 Column visibility

- Right-clicking headers opens a checkable menu: one item per column, checked = visible.
- Column locking uses an attached property on the column
  (`behaviors:GridColumnVisibility.Locked="True"`), replacing the localized-string comparison bug.
  Locked columns render as disabled menu items.
- Invariant: at least the locked column(s) remain visible; enforced by construction.
- Toggling sets `column.Visibility` to `Visible`/`Collapsed`; the menu reflects live visibility each time it opens.

### 4.4 Clipboard copy

- Menu item executes `BetterDataGrid.CopyRowsCommand`.
- The grid joins selected rows' formatter output with `Environment.NewLine` and copies via
  `Clipboard.SetDataObject(text, retry: true)`.
- `ProcessRowFormatter` output is byte-identical to today's tab-delimited row text.

### 4.5 Preserved limitation

Polling refresh replaces process items; replaced items start unselected. Same as current behavior; explicitly out of scope.

## 5. Error Handling & Edge Cases

- **Clipboard contention**: `SetDataObject(text, retry: true)` absorbs transient locks; residual `COMException`
  is caught and silently dropped (copy is a convenience action; retry covers realistic contention). Documented in code.
- **Empty selection copy**: `CopyRowsCommand.CanExecute` is false with zero selected rows; the menu item auto-disables
  via native command plumbing.
- **Null items in formatting**: formatter receives `null` → returns `string.Empty`.
- **Column visibility invariant**: guaranteed by locked columns; no runtime guard needed.
- **Selection during refresh**: `ItemContainerStyle` binding re-evaluates per container; no manual sync code exists to break.
- **VM command preconditions**: Terminate/SetPriority keep existing validation (`SelectProcess` error dialog when
  nothing selected) — unchanged contract; its automated coverage belongs to the follow-up testing effort (§2).

## 6. Testing Strategy

### 6.1 Infrastructure

- `Xunit.StaFact` `[WpfFact]`: real controls on an STA thread; no application launch.
- `InternalsVisibleTo "TaskManager.Tests"` already present; controls stay `internal`.
- Test harness (`GridTestHost`) instantiates `BetterDataGrid`, assigns an `ItemsSource` of real `ProcessItem`s,
  and forces layout so row containers exist. No window is shown.

### 6.2 Input simulation

Right-clicks are raised via `RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice,
Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.MouseRightButtonDownEvent })` against specific
rows — the same event path a user's click takes. No reflection into internals, no sleeps, no real timers.

### 6.3 Coverage map

| Test file | Verifies (observable outcomes only) |
|---|---|
| `SelectionUnificationTests` | checkbox tick → row selected & model true; Ctrl-click multi-select → both boxes checked; model change → highlight follows; replaced item starts unselected |
| `RightClickSemanticsTests` | the four scenarios in §4.2, plus menu opens with `PlacementTarget` = grid |
| `ColumnVisibilityTests` | one checkable item per column; locked item disabled; toggle hides/shows; live state each open |
| `ClipboardCopyTests` | single & multi-row copy text correct; injected formatter honored; zero selection → command cannot execute |
| `ProcessRowFormatterTests` | pure unit: tab-delimited output byte-identical to today's |

Clipboard tests save and restore system clipboard state to stay side-effect-free.

### 6.4 Verification plan

- Full solution build (`dotnet build`).
- Entire test suite green (`dotnet test`), including new UI tests.
- Manual smoke run: launch app, verify row menu actions (Monitor/Copy/Priority/Terminate), header menu toggling,
  checkbox/row selection unity, and Polish locale rendering (column-lock fix is locale-proof).

## 7. Non-Goals & Future Work

- Grid feature work (sorting/filter/persistence) — separate spec if desired.
- ViewModel/ProcessManager test seams — follow-up testing effort.
- FlaUI end-to-end automation — revisit only if STA coverage proves insufficient.
