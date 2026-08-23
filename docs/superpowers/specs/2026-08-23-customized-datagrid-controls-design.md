> **SUPERSEDED** by `2026-08-23-datagrid-controls-redesign-design.md` — retained for history only.

# Design Specification: Customized DataGrid Controls Architecture

## 1. Overview & Objectives
This specification outlines the optimal design for customized `DataGrid` controls within the Task Manager application. 
The primary goals are:
- Transition from rigid inheritance hierarchies (`BaseBetterDataGrid` -> `BetterDataGrid` -> `ProcessDataGrid`) toward **composition via attached behaviors and modular helper services**.
- Separate generic UI grid enhancements (column visibility management, context menu handling, clipboard copying, row selection synchronization) from domain-specific actions (process termination, priority setting, monitoring).
- Ensure full MVVM compliance and robust testability.

---

## 2. Architecture & Components

### 2.1. `BetterDataGrid` (Generic Base Control)
- Inherits directly from `System.Windows.Controls.DataGrid`.
- **Responsibilities**:
  - Right-click detection on rows vs. column headers.
  - Automatic column header context menu generation (toggle column visibility, support for locking key columns like Name).
  - Row selection enforcement on right-click.

### 2.2. Attached Behaviors & Modularity
- **`DataGridContextMenuBehavior`**: Manages context menu registration and command forwarding for rows.
- **`DataGridClipboardBehavior`**: Encapsulates copying row data (supporting custom formatters like tab-delimited serialization) to the system clipboard.
- **`CheckBoxClickBehavior`**: Synchronizes checkbox states in template columns with row selection and underlying model properties (`ProcessItem.IsSelected`).

### 2.3. Domain Integration
- Domain-specific grids (such as `ProcessDataGrid`) are configured either as thin subclasses or standard `BetterDataGrid` instances augmented with domain command bindings (`SetPriorityCommand`, `TerminateCommand`, `MonitorCommand`).

---

## 3. Detailed Design & Implementation Steps

1. **Refactor `BetterDataGrid`**:
   - Make `BetterDataGrid` self-contained without abstract subclassing requirements for basic features.
   - Extract domain-specific context menu logic into behaviors or configurable properties.
2. **Enhance Behaviors**:
   - Ensure `CheckBoxClickBehavior` and new context menu behaviors cleanly handle preview events and MVVM commands.
3. **Validation & Testing**:
   - Verify selection synchronization, clipboard copying, and column visibility toggling via unit and integration tests.

---

## 4. Verification Plan
- Run existing test suites (`TaskManager.Tests`).
- Verify application builds and runs successfully without regressions in process management or UI interactions.
