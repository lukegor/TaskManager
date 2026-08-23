# Customized DataGrid Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor and optimize the customized DataGrid control architecture in Task Manager to use modular behaviors and a clean generic `BetterDataGrid` base, eliminating rigid inheritance chains while preserving all context menu, clipboard, and process management features.

**Architecture:** 
- Decouple generic grid capabilities (column visibility management, selection synchronization, clipboard copy) from domain-specific actions (process termination, priority management, monitoring).
- Structure via `BetterDataGrid` (generic base) and reusable attached behaviors (`DataGridContextMenuBehavior`, `CheckBoxClickBehavior`).

**Tech Stack:** C# .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft.Xaml.Behaviors.Wpf.

## Global Constraints
- Target Framework: .NET 8.0 Windows.
- Keep all localization keys (`x:Static resx:Strings...`) intact.
- Maintain existing MVVM bindings and command flows.

---

## File Structure
- **Modify**: `TaskManager/UI/Controls/BetterDataGrid.cs`
- **Modify**: `TaskManager/UI/Controls/ProcessDataGrid.cs`

---

### Task 1: Refactor `BetterDataGrid` and Decouple Domain Logic

**Files:**
- Modify: `TaskManager/UI/Controls/BetterDataGrid.cs`
- Modify: `TaskManager/UI/Controls/ProcessDataGrid.cs`

- [ ] **Step 1: Update `BetterDataGrid.cs` and `ProcessDataGrid.cs` and build solution.**
- [ ] **Step 2: Run tests and commit.**
