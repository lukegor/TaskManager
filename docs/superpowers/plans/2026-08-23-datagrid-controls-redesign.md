# DataGrid Controls Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `BaseBetterDataGrid` → `BetterDataGrid` → `ProcessDataGrid` inheritance chain with one composition-based generic control plus behaviors/formatters, unify selection into a single model-backed source of truth, and lock every behavioral contract in behind a deterministic STA test suite.

**Architecture:** A single concrete `BetterDataGrid : DataGrid` owns right-click routing (Windows-standard selection semantics), view-declared row-context-menu hosting, and routed-command clipboard copying with an injected `IRowTextFormatter`. Column visibility becomes a static attached-property behavior (`GridColumnVisibility`) with per-column locking. `ProcessItem` gains `INotifyPropertyChanged` so `IsSelected` is the single source of truth bound TwoWay by both the row style and the checkbox. Spec: `docs/superpowers/specs/2026-08-23-datagrid-controls-redesign-design.md`.

**Tech Stack:** C# .NET 8.0-windows, WPF, CommunityToolkit.Mvvm, Microsoft.Xaml.Behaviors.Wpf (already referenced; no new packages), xunit.v3 + Xunit.StaFact (`[WpfFact]`) + NSubstitute.

## Global Constraints

- Target framework: `.NET 8.0-windows`; WPF (`UseWPF`); no new NuGet packages.
- All localization keys stay as-is (`x:Static resx:Strings.*` values must not change).
- Controls stay `internal`; tests rely on the existing `InternalsVisibleTo "TaskManager.Tests"`.
- Clipboard output must be byte-identical to today: `Process.ToDelimitedString('\t')` per row, joined with `Environment.NewLine`.
- Polling refresh resets selection for replaced items — accepted preserved limitation, do not "fix".
- VM command contracts (`TerminateProcesses`, `SetPriority`, their `ValidatePreconditions` dialogs) are unchanged.
- Test behavior, not implementation: drive real events (`RaiseEvent`) and assert observable outcomes; no reflection into private members, no sleeps, no real timers.

---

## File Structure (final state)

```
TaskManager/
├── UI/Behaviors/GridColumnVisibility.cs          (new — replaces header-menu logic from deleted Base*)
├── UI/Controls/BetterDataGrid.cs                 (rewritten — sole control; Base*/Process* deleted)
├── UI/Formatters/IRowTextFormatter.cs            (new)
├── UI/Formatters/ProcessRowFormatter.cs          (new)
├── UI/Behaviors/CheckBoxClickBehavior.cs         (deleted)
├── UI/Views/MainWindow.xaml                      (rewired)
├── ViewModels/MainWindowViewModel.cs             (dead selection plumbing removed)
TaskManager.Domain/Models/ProcessItem.cs          (gains INotifyPropertyChanged)
TaskManager.Tests/
├── TestSupport/GridTestHost.cs                   (new)
├── Models/ProcessItemTests.cs                    (new)
├── UI_Behaviors/ColumnVisibilityTests.cs         (new)
└── UI_Controls/
    ├── SelectionUnificationTests.cs              (new)
    ├── RightClickSemanticsTests.cs               (new)
    └── ClipboardCopyTests.cs                     (new)
```

---

### Task 1: `ProcessItem` selection notification (single source of truth)

**Files:**
- Modify: `TaskManager.Domain/Models/ProcessItem.cs`
- Test: `TaskManager.Tests/Models/ProcessItemTests.cs`

**Interfaces:**
- Consumes: existing `Process` domain model.
- Produces: `ProcessItem : INotifyPropertyChanged` with `bool IsSelected` raising `PropertyChanged` on change only. Later tasks bind `DataGridRow.IsSelected` and `CheckBox.IsChecked` TwoWay to `IsSelected`.

- [ ] **Step 1: Implement INotifyPropertyChanged**

Replace the entire content of `TaskManager.Domain/Models/ProcessItem.cs` with:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Models.Process wrapper with wider logic.
    /// <see cref="IsSelected"/> is the single source of truth for row selection:
    /// both the DataGrid row style and the select-checkbox bind to it TwoWay.
    /// </summary>
    public class ProcessItem : INotifyPropertyChanged
    {
        private bool isSelected;

        public Process Process { get; set; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProcessItem()
        {
        }

        public ProcessItem(Process process) : this()
        {
            Process = process;
        }

        public override string ToString()
        {
            return $"{Process.Name} ({Process.Pid})";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Add the expected tests**

Create `TaskManager.Tests/Models/ProcessItemTests.cs`:

```csharp
using TaskManager.Domain.Models;

namespace TaskManager.Tests.Models
{
    public class ProcessItemTests
    {
        [Fact]
        public void IsSelected_Change_RaisesPropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1 });
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.IsSelected = true;

            Assert.True(item.IsSelected);
            Assert.Contains(nameof(ProcessItem.IsSelected), raised);
        }

        [Fact]
        public void IsSelected_SameValue_DoesNotRaisePropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1 }) { IsSelected = true };
            var raised = 0;
            item.PropertyChanged += (_, _) => raised++;

            item.IsSelected = true;

            Assert.Equal(0, raised);
        }
    }
}
```

- [ ] **Step 3: Run relevant validation**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ProcessItemTests"
```

Both tests must pass; the full solution must build.

- [ ] **Step 4: Commit**

```bash
git add TaskManager.Domain/Models/ProcessItem.cs TaskManager.Tests/Models/ProcessItemTests.cs
git commit -m "feat(domain): make ProcessItem.IsSelected notifyPropertyChanged as selection source of truth"
```

---

### Task 2: Composition migration — new control, formatters, view rewiring, VM cleanup

This is the atomic swap: after this task the old hierarchy is gone and the app builds and behaves identically except for the temporarily-absent column-header menu (restored by Task 3).

**Files:**
- Create: `TaskManager/UI/Formatters/IRowTextFormatter.cs`
- Create: `TaskManager/UI/Formatters/ProcessRowFormatter.cs`
- Rewrite: `TaskManager/UI/Controls/BetterDataGrid.cs`
- Delete: `TaskManager/UI/Controls/BaseBetterDataGrid.cs`
- Delete: `TaskManager/UI/Controls/ProcessDataGrid.cs`
- Delete: `TaskManager/UI/Behaviors/CheckBoxClickBehavior.cs`
- Modify: `TaskManager/UI/Views/MainWindow.xaml`
- Modify: `TaskManager/ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `ProcessItem.IsSelected` (INPC, Task 1); `VisualTreeUtilityHelper.FindVisualParent<T>/GetVisualChild<T>` (existing); localization keys `Strings.Select/Name/Pid/Architecture/Path/Priority/ThreadCount/Ppid/Monitor/CopyToClipboard/SetPriority/Terminate` (existing).
- Produces (used by Tasks 3–4 and XAML):
  - `internal class BetterDataGrid : DataGrid`
  - `public static readonly RoutedCommand CopyRowsCommand` (executes against any `BetterDataGrid`)
  - `ContextMenu RowContextMenu { get; set; }` — dependency property, view-declared row menu
  - `IRowTextFormatter? RowFormatter { get; set; }` — dependency property; null → `ToString()` fallback
  - `internal static bool IsPointInColumnHeaders(DataGrid grid, Point point)`
  - `interface IRowTextFormatter { string Format(object? item); }`
  - `class ProcessRowFormatter : IRowTextFormatter`

- [ ] **Step 1: Create `IRowTextFormatter`**

Create `TaskManager/UI/Formatters/IRowTextFormatter.cs`:

```csharp
namespace TaskManager.UI.Formatters
{
    /// <summary>
    /// Converts a grid row's bound data object into clipboard text.
    /// Keeps the generic grid free of domain knowledge.
    /// </summary>
    public interface IRowTextFormatter
    {
        string Format(object? item);
    }
}
```

- [ ] **Step 2: Create `ProcessRowFormatter`**

Create `TaskManager/UI/Formatters/ProcessRowFormatter.cs`:

```csharp
using TaskManager.Domain.Models;

namespace TaskManager.UI.Formatters
{
    /// <summary>
    /// Formats a <see cref="ProcessItem"/> as its tab-delimited process text,
    /// byte-identical to the pre-refactor copy output. Anything else falls back to ToString().
    /// </summary>
    public class ProcessRowFormatter : IRowTextFormatter
    {
        public string Format(object? item)
        {
            return item is ProcessItem processItem
                ? processItem.Process.ToDelimitedString('\t')
                : item?.ToString() ?? string.Empty;
        }
    }
}
```

- [ ] **Step 3: Rewrite `BetterDataGrid`**

Replace the entire content of `TaskManager/UI/Controls/BetterDataGrid.cs` with:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TaskManager.UI.Formatters;
using TaskManager.Utility.Utility;

namespace TaskManager.UI.Controls
{
    /// <summary>
    /// Generic DataGrid with Windows-standard right-click selection semantics,
    /// a view-declared row context menu, and routed copy-to-clipboard support.
    /// Contains no domain knowledge: row formatting is delegated to an injected
    /// <see cref="IRowTextFormatter"/>, column-header menus live in GridColumnVisibility.
    /// </summary>
    internal class BetterDataGrid : DataGrid
    {
        public static readonly RoutedCommand CopyRowsCommand =
            new RoutedCommand(nameof(CopyRowsCommand), typeof(BetterDataGrid));

        public static readonly DependencyProperty RowContextMenuProperty = DependencyProperty.Register(
            nameof(RowContextMenu),
            typeof(ContextMenu),
            typeof(BetterDataGrid),
            new PropertyMetadata(null));

        public static readonly DependencyProperty RowFormatterProperty = DependencyProperty.Register(
            nameof(RowFormatter),
            typeof(IRowTextFormatter),
            typeof(BetterDataGrid),
            new PropertyMetadata(null));

        /// <summary>View-declared menu opened on right-click over a data row.</summary>
        public ContextMenu RowContextMenu
        {
            get => (ContextMenu)GetValue(RowContextMenuProperty);
            set => SetValue(RowContextMenuProperty, value);
        }

        /// <summary>Converts selected row objects to clipboard text. Null falls back to ToString().</summary>
        public IRowTextFormatter? RowFormatter
        {
            get => (IRowTextFormatter?)GetValue(RowFormatterProperty);
            set => SetValue(RowFormatterProperty, value);
        }

        public BetterDataGrid()
        {
            CommandBindings.Add(new CommandBinding(CopyRowsCommand, OnCopyRowsExecuted, OnCopyRowsCanExecute));
            MouseRightButtonDown += OnMouseRightButtonDown;
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataGridRow clickedRow = VisualTreeUtilityHelper.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (clickedRow == null)
            {
                return; // header clicks are owned by GridColumnVisibility; empty areas do nothing
            }

            // Windows-standard: collapse the selection only when the clicked row was not part of it
            if (!clickedRow.IsSelected)
            {
                SelectedItems.Clear();
                clickedRow.IsSelected = true;
            }

            OpenRowContextMenu();
        }

        private void OpenRowContextMenu()
        {
            ContextMenu menu = RowContextMenu;
            if (menu == null)
            {
                return;
            }

            if (menu.IsOpen)
            {
                menu.IsOpen = false;
            }

            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void OnCopyRowsExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            string text = string.Join(Environment.NewLine, SelectedItems.Cast<object>().Select(FormatRow));
            try
            {
                Clipboard.SetDataObject(text, retry: true);
            }
            catch (COMException)
            {
                // clipboard still held by another process after internal retries; drop silently
            }
        }

        private void OnCopyRowsCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = SelectedItems.Count > 0;
        }

        private string FormatRow(object item)
        {
            return RowFormatter?.Format(item) ?? item?.ToString() ?? string.Empty;
        }

        internal static bool IsPointInColumnHeaders(DataGrid grid, Point point)
        {
            var headersPresenter = VisualTreeUtilityHelper.GetVisualChild<DataGridColumnHeadersPresenter>(grid);
            if (headersPresenter != null)
            {
                Rect headersRect = VisualTreeHelper.GetDescendantBounds(headersPresenter);
                return headersRect.Contains(point);
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: Delete obsolete controls and behavior**

```bash
git rm "TaskManager/UI/Controls/BaseBetterDataGrid.cs" "TaskManager/UI/Controls/ProcessDataGrid.cs" "TaskManager/UI/Behaviors/CheckBoxClickBehavior.cs"
```

- [ ] **Step 5: Rewire `MainWindow.xaml`**

In `TaskManager/UI/Views/MainWindow.xaml`:

1. Add the formatters namespace next to the existing ones (keep `xmlns:b` — Task 3 reuses it):

```xml
xmlns:fmt="clr-namespace:TaskManager.UI.Formatters"
```

2. Replace the entire `<controls:ProcessDataGrid ...>...</controls:ProcessDataGrid>` element (lines 79–105 of the current file) with:

```xml
<controls:BetterDataGrid
    IsReadOnly="True" AutoGenerateColumns="False"
    ItemsSource="{Binding Processes, Mode=TwoWay}" Margin="-5,0,0,0">
    <controls:BetterDataGrid.RowFormatter>
        <fmt:ProcessRowFormatter/>
    </controls:BetterDataGrid.RowFormatter>
    <!-- NOTE: Monitor intentionally binds to MonitorButtonCommand which the VM does not expose yet —
         identical to the pre-refactor inert behavior; wiring real monitoring is out of scope. -->
    <controls:BetterDataGrid.RowContextMenu>
        <ContextMenu>
            <MenuItem Header="{x:Static resx:Strings.Monitor}" Command="{Binding MonitorButtonCommand}"/>
            <MenuItem Header="{x:Static resx:Strings.CopyToClipboard}" Command="controls:BetterDataGrid.CopyRows"/>
            <MenuItem Header="{x:Static resx:Strings.SetPriority}" Command="{Binding SetPriorityCommand}"/>
            <MenuItem Header="{x:Static resx:Strings.Terminate}" Command="{Binding TerminateCommand}"/>
        </ContextMenu>
    </controls:BetterDataGrid.RowContextMenu>
    <controls:BetterDataGrid.ItemContainerStyle>
        <Style TargetType="DataGridRow">
            <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}"/>
        </Style>
    </controls:BetterDataGrid.ItemContainerStyle>
    <controls:BetterDataGrid.Columns>
        <DataGridTemplateColumn Header="{x:Static resx:Strings.Select}">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" HorizontalAlignment="Center"/>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
        <DataGridTextColumn Header="{x:Static resx:Strings.Name}" Binding="{Binding Process.Name}" />
        <DataGridTextColumn Header="{x:Static resx:Strings.Pid}" Binding="{Binding Process.Pid}" />
        <DataGridTextColumn Header="{x:Static resx:Strings.Architecture}" Binding="{Binding Process.ArchitectureTypeDisplay}" />
        <DataGridTextColumn Header="{x:Static resx:Strings.Path}" Binding="{Binding Process.Path}" Width="200" />
        <DataGridTextColumn Header="{x:Static resx:Strings.Priority}" Binding="{Binding Process.Priority}"/>
        <DataGridTextColumn Header="{x:Static resx:Strings.ThreadCount}" Binding="{Binding Process.ThreadCount}"/>
        <DataGridTextColumn Header="{x:Static resx:Strings.Ppid}" Binding="{Binding Process.Ppid}"/>
    </controls:BetterDataGrid.Columns>
</controls:BetterDataGrid>
```

Key changes vs. the old markup: no `SetPriorityCommand`/`TerminateCommand`/`MonitorCommand` attributes, no `i:Interaction.Triggers` SelectionChanged block, no `b:CheckBoxClickBehavior`, checkbox binds its own `DataContext` instead of the ancestor row, row style unifies selection, `b:GridColumnVisibility.IsEnabled` is deliberately NOT added yet (Task 3).

- [ ] **Step 6: Clean up `MainWindowViewModel`**

In `TaskManager/ViewModels/MainWindowViewModel.cs`:

1. Remove the `using System.Windows.Controls;` line.
2. Delete the `public ICommand BetterDataGrid_SelectionChangedCommand { get; private set; }` property.
3. In `BindFunctionsToCommands()`, delete the line assigning `BetterDataGrid_SelectionChangedCommand`.
4. Delete the whole `BetterDataGrid_OnSelectionChanged` method (including its thread-count/threadpool debug lines).

Do not touch anything else — `MonitoringButtonIcon`, `SelectedTabIndex`, all other commands and synchronizers stay.

- [ ] **Step 7: Run relevant validation**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests
```

Full existing suite (VVM naming tests, ProcessManager integration tests) must pass. Then launch the app manually once: rows render, left-click/ctrl-click selection works, checkboxes mirror selection, right-click opens the row menu, Copy puts tab-delimited text on the clipboard, SetPriority/Terminate behave as before. (Column-header right-click does nothing yet — restored in Task 3.)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(ui): replace DataGrid inheritance chain with composition-based control"
```

---

### Task 3: `GridColumnVisibility` behavior (header menu + locking)

**Files:**
- Create: `TaskManager/UI/Behaviors/GridColumnVisibility.cs`
- Modify: `TaskManager/UI/Views/MainWindow.xaml` (attach behavior + lock Name)
- Test: `TaskManager.Tests/UI_Behaviors/ColumnVisibilityTests.cs`

**Interfaces:**
- Consumes: `BetterDataGrid.IsPointInColumnHeaders(DataGrid, Point)` (Task 2); `VisualTreeUtilityHelper.FindVisualParent<T>` (existing).
- Produces (used by tests): attached properties `GridColumnVisibility.IsEnabled` (on `DataGrid`) and `GridColumnVisibility.Locked` (on `DataGridColumn`); `internal static ContextMenu BuildHeaderMenu(DataGrid grid)`; `internal static ContextMenu ShowHeaderMenu(DataGrid grid)` (builds, anchors to grid, opens).

- [ ] **Step 1: Implement the behavior**

Create `TaskManager/UI/Behaviors/GridColumnVisibility.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TaskManager.UI.Controls;
using TaskManager.Utility.Utility;

namespace TaskManager.UI.Behaviors
{
    /// <summary>
    /// Column-header right-click menu for toggling column visibility.
    /// Columns marked Locked render disabled and can never be hidden,
    /// guaranteeing at least one visible column at all times.
    /// </summary>
    public static class GridColumnVisibility
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(GridColumnVisibility),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty LockedProperty = DependencyProperty.RegisterAttached(
            "Locked",
            typeof(bool),
            typeof(GridColumnVisibility),
            new PropertyMetadata(false));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static bool GetLocked(DependencyObject obj) => (bool)obj.GetValue(LockedProperty);
        public static void SetLocked(DependencyObject obj, bool value) => obj.SetValue(LockedProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                grid.MouseRightButtonDown += OnGridRightButtonDown;
            }
            else
            {
                grid.MouseRightButtonDown -= OnGridRightButtonDown;
            }
        }

        private static void OnGridRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var grid = (DataGrid)sender;

            if (VisualTreeUtilityHelper.FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) != null)
            {
                return; // row clicks belong to BetterDataGrid
            }

            if (!BetterDataGrid.IsPointInColumnHeaders(grid, e.GetPosition(grid)))
            {
                return; // empty area
            }

            ShowHeaderMenu(grid);
        }

        /// <summary>Builds the header visibility menu, anchors it to the grid at the mouse position, and opens it.</summary>
        internal static ContextMenu ShowHeaderMenu(DataGrid grid)
        {
            ContextMenu menu = BuildHeaderMenu(grid);
            menu.PlacementTarget = grid;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            return menu;
        }

        internal static ContextMenu BuildHeaderMenu(DataGrid grid)
        {
            var menu = new ContextMenu();

            foreach (DataGridColumn column in grid.Columns)
            {
                var menuItem = new MenuItem
                {
                    Header = column.Header,
                    IsCheckable = true,
                    IsChecked = column.Visibility == Visibility.Visible,
                    IsEnabled = !GetLocked(column)
                };

                // closure captures column + item; no Tag plumbing needed
                menuItem.Click += (_, _) =>
                {
                    column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                };

                menu.Items.Add(menuItem);
            }

            return menu;
        }
    }
}
```

- [ ] **Step 2: Attach the behavior in XAML**

In `TaskManager/UI/Views/MainWindow.xaml`:

1. On the `<controls:BetterDataGrid ...>` element add the attribute:

```xml
b:GridColumnVisibility.IsEnabled="True"
```

2. On the Name column add:

```xml
<DataGridTextColumn Header="{x:Static resx:Strings.Name}" Binding="{Binding Process.Name}"
                    b:GridColumnVisibility.Locked="True" />
```

(The `xmlns:b` prefix already resolves to `clr-namespace:TaskManager.UI.Behaviors`.)

- [ ] **Step 3: Add the expected tests**

Create `TaskManager.Tests/UI_Behaviors/ColumnVisibilityTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using TaskManager.UI.Behaviors;

namespace TaskManager.Tests.UI_Behaviors
{
    public class ColumnVisibilityTests
    {
        private static DataGrid CreateGridWithColumns()
        {
            var grid = new DataGrid { AutoGenerateColumns = false };
            grid.Columns.Add(new DataGridTextColumn { Header = "Name" });
            GridColumnVisibility.SetLocked(grid.Columns[0], true);
            grid.Columns.Add(new DataGridTextColumn { Header = "Pid" });
            grid.Columns.Add(new DataGridTextColumn { Header = "Priority" });
            return grid;
        }

        [WpfFact]
        public void BuildHeaderMenu_OneCheckableItemPerColumn()
        {
            DataGrid grid = CreateGridWithColumns();

            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);

            Assert.Equal(3, menu.Items.Count);
            foreach (MenuItem item in menu.Items.Cast<MenuItem>())
            {
                Assert.True(item.IsCheckable);
            }
        }

        [WpfFact]
        public void BuildHeaderMenu_LockedColumn_DisabledAndCheckedWhenVisible()
        {
            DataGrid grid = CreateGridWithColumns();

            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);

            MenuItem lockedItem = (MenuItem)menu.Items[0];
            Assert.False(lockedItem.IsEnabled);
            Assert.True(lockedItem.IsChecked);
        }

        [WpfFact]
        public void Click_VisibleUnlockedItem_HidesItsColumn()
        {
            DataGrid grid = CreateGridWithColumns();
            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);
            var pidItem = (MenuItem)menu.Items[1];

            pidItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(Visibility.Collapsed, grid.Columns[1].Visibility);
            Assert.False(pidItem.IsChecked);
        }

        [WpfFact]
        public void Click_HiddenColumnItem_ShowsColumnAgain()
        {
            DataGrid grid = CreateGridWithColumns();
            grid.Columns[2].Visibility = Visibility.Collapsed;
            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);
            var priorityItem = (MenuItem)menu.Items[2];
            Assert.False(priorityItem.IsChecked);

            priorityItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(Visibility.Visible, grid.Columns[2].Visibility);
        }

        [WpfFact]
        public void ShowHeaderMenu_OpensAnchoredToGrid()
        {
            DataGrid grid = CreateGridWithColumns();

            ContextMenu menu = GridColumnVisibility.ShowHeaderMenu(grid);

            Assert.True(menu.IsOpen);
            Assert.Same(grid, menu.PlacementTarget);
            Assert.Equal(PlacementMode.MousePoint, menu.Placement);
            menu.IsOpen = false;
        }
    }
}
```

Rationale recorded for reviewers: the branch decision "which visual was clicked" is geometry-dependent and cannot be made deterministic off-screen, so tests exercise the menu composition/open/toggle contracts (`BuildHeaderMenu`/`ShowHeaderMenu`/item clicks); cursor-position accuracy is verified in the manual smoke run (Global Constraints §6.4 of the spec).

- [ ] **Step 4: Run relevant validation**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests --filter "FullyQualifiedName~ColumnVisibilityTests"
```

All five tests pass. Manual smoke: right-clicking column headers opens the visibility menu at the cursor; the Name item is greyed out; hiding/showing Pid works; Polish locale still renders correct headers and the lock survives language switching (this was the localized-string bug).

- [ ] **Step 5: Commit**

```bash
git add TaskManager/UI/Behaviors/GridColumnVisibility.cs TaskManager/UI/Views/MainWindow.xaml TaskManager.Tests/UI_Behaviors/ColumnVisibilityTests.cs
git commit -m "feat(ui): add GridColumnVisibility behavior with locale-proof column locking"
```

---

### Task 4: STA behavior tests — selection unity, right-click semantics, clipboard

**Files:**
- Create: `TaskManager.Tests/TestSupport/GridTestHost.cs`
- Create: `TaskManager.Tests/UI_Controls/SelectionUnificationTests.cs`
- Create: `TaskManager.Tests/UI_Controls/RightClickSemanticsTests.cs`
- Create: `TaskManager.Tests/UI_Controls/ClipboardCopyTests.cs`

**Interfaces:**
- Consumes: everything produced in Tasks 1–3; `[WpfFact]` from Xunit.StaFact; `InternalsVisibleTo` for internal controls.
- Produces (reused across these three test files): `GridTestHost.CreateItems`, `CreateGrid`, `ApplyUnifiedSelectionStyle`, `GetRow`, `RaiseRightClick`.

- [ ] **Step 1: Create the test host**

Create `TaskManager.Tests/TestSupport/GridTestHost.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TaskManager.Domain.Models;
using TaskManager.UI.Controls;

namespace TaskManager.Tests.TestSupport
{
    /// <summary>
    /// Builds fully laid-out BetterDataGrid instances off-screen so row containers exist
    /// and real input events can be raised against them. No window is ever shown.
    /// </summary>
    internal static class GridTestHost
    {
        private static readonly Size LayoutSize = new(400, 300);

        public static List<ProcessItem> CreateItems(params (string Name, int Pid)[] processes)
        {
            return processes
                .Select(p => new ProcessItem(new Process { Name = p.Name, Pid = p.Pid }))
                .ToList();
        }

        public static BetterDataGrid CreateGrid(IList<ProcessItem> items)
        {
            var grid = new BetterDataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new ObservableCollection<ProcessItem>(items)
            };

            // forcing measure/arrange realizes DataGridRow containers without showing anything
            var container = new Border { Child = grid };
            container.Measure(LayoutSize);
            container.Arrange(new Rect(LayoutSize));
            grid.UpdateLayout();

            return grid;
        }

        /// <summary>Mirrors the production ItemContainerStyle declared in MainWindow.xaml.</summary>
        public static void ApplyUnifiedSelectionStyle(BetterDataGrid grid)
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(DataGridRow.IsSelectedProperty,
                new Binding(nameof(ProcessItem.IsSelected)) { Mode = BindingMode.TwoWay }));
            grid.ItemContainerStyle = style;
            grid.UpdateLayout();
        }

        public static DataGridRow GetRow(BetterDataGrid grid, int index)
        {
            return (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index)!;
        }

        public static void RaiseRightClick(FrameworkElement target)
        {
            target.RaiseEvent(new MouseButtonEventArgs(
                InputManager.Current.PrimaryMouseDevice,
                Environment.TickCount,
                MouseButton.Right)
            {
                RoutedEvent = Mouse.MouseRightButtonDownEvent
            });
        }
    }
}
```

Troubleshooting note for the executor: if `GetRow` ever returns null (virtualization did not realize containers), first confirm `container.ActualHeight > 0`; as a fallback set `VirtualizingPanel.VirtualizationMode="Standard"` on the grid inside `CreateGrid`. Do not introduce sleeps.

- [ ] **Step 2: Selection unification tests**

Create `TaskManager.Tests/UI_Controls/SelectionUnificationTests.cs`. Note on scope: ctrl-click multi-select mechanics are framework-owned; the app-owned contract is the TwoWay binding, exercised identically by programmatic `IsSelected` writes on either side. Checkbox ticks hit the very same property, so they are covered transitively.

```csharp
using System.Collections.ObjectModel;
using System.Windows.Controls;
using TaskManager.Domain.Models;
using TaskManager.Tests.TestSupport;

namespace TaskManager.Tests.UI_Controls
{
    public class SelectionUnificationTests
    {
        [WpfFact]
        public void RowSelection_WritesThroughToModel()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);

            GridTestHost.GetRow(grid, 0).IsSelected = true;

            Assert.True(items[0].IsSelected);
            Assert.Contains(items[0], grid.SelectedItems.Cast<ProcessItem>());
        }

        [WpfFact]
        public void MultipleRowsSelected_AllModelsReflectIt()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);

            GridTestHost.GetRow(grid, 0).IsSelected = true;
            GridTestHost.GetRow(grid, 2).IsSelected = true;

            Assert.True(items[0].IsSelected);
            Assert.True(items[2].IsSelected);
            Assert.False(items[1].IsSelected);
            Assert.Equal(2, grid.SelectedItems.Count);
        }

        [WpfFact]
        public void ModelChange_UpdatesRowHighlight()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);

            items[1].IsSelected = true;

            Assert.True(GridTestHost.GetRow(grid, 1).IsSelected);
        }

        [WpfFact]
        public void ModelDeselect_ClearsRowHighlight()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            Assert.True(items[1].IsSelected);

            items[1].IsSelected = false;

            Assert.False(GridTestHost.GetRow(grid, 1).IsSelected);
        }

        [WpfFact]
        public void ReplacedItem_StartsUnselected()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            Assert.True(items[1].IsSelected);

            var collection = (ObservableCollection<ProcessItem>)grid.ItemsSource;
            collection[1] = new ProcessItem(new Process { Name = "replacement", Pid = 99 });
            grid.UpdateLayout();

            Assert.False(collection[1].IsSelected);
            Assert.False(GridTestHost.GetRow(grid, 1).IsSelected);
        }
    }
}
```

- [ ] **Step 3: Right-click semantics tests**

Create `TaskManager.Tests/UI_Controls/RightClickSemanticsTests.cs`:

```csharp
using System.Windows.Controls;
using TaskManager.Domain.Models;
using TaskManager.Tests.TestSupport;
using TaskManager.UI.Controls;

namespace TaskManager.Tests.UI_Controls
{
    public class RightClickSemanticsTests : IDisposable
    {
        private ContextMenu? _openMenu;

        public void Dispose()
        {
            if (_openMenu != null)
            {
                _openMenu.IsOpen = false;
            }
        }

        [WpfFact]
        public void RightClick_UnselectedRow_CollapsesSelectionToIt_AndOpensRowMenu()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 0).IsSelected = true;
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            _openMenu = new ContextMenu();
            grid.RowContextMenu = _openMenu;

            GridTestHost.RaiseRightClick(GridTestHost.GetRow(grid, 2));

            Assert.Equal(new[] { items[2] }, grid.SelectedItems.Cast<ProcessItem>());
            Assert.True(_openMenu.IsOpen);
            Assert.Same(grid, _openMenu.PlacementTarget);
        }

        [WpfFact]
        public void RightClick_RowAlreadyInMultiSelection_PreservesSelection_AndOpensMenu()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 0).IsSelected = true;
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            _openMenu = new ContextMenu();
            grid.RowContextMenu = _openMenu;

            GridTestHost.RaiseRightClick(GridTestHost.GetRow(grid, 0));

            Assert.Equal(new[] { items[0], items[1] }, grid.SelectedItems.Cast<ProcessItem>());
            Assert.True(_openMenu.IsOpen);
        }

        [WpfFact]
        public void RightClick_NeitherRowNorAssignedMenu_NothingHappens()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 0).IsSelected = true;

            // OriginalSource = the grid itself: resolves to no DataGridRow, and no RowContextMenu is set
            GridTestHost.RaiseRightClick(grid);

            Assert.Equal(new[] { items[0] }, grid.SelectedItems.Cast<ProcessItem>());
            Assert.Null(grid.ContextMenu);
        }
    }
}
```

- [ ] **Step 4: Clipboard tests**

Create `TaskManager.Tests/UI_Controls/ClipboardCopyTests.cs`:

```csharp
using System.Windows;
using TaskManager.Domain.Models;
using TaskManager.Tests.TestSupport;
using TaskManager.UI.Controls;
using TaskManager.UI.Formatters;

namespace TaskManager.Tests.UI_Controls
{
    public class ClipboardCopyTests : IDisposable
    {
        private readonly string _previousClipboard;

        public ClipboardCopyTests()
        {
            _previousClipboard = Clipboard.GetText();
        }

        public void Dispose()
        {
            if (_previousClipboard.Length > 0)
            {
                Clipboard.SetText(_previousClipboard);
            }
        }

        [WpfFact]
        public void NoSelection_CopyCannotExecute()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);

            Assert.False(BetterDataGrid.CopyRowsCommand.CanExecute(null, grid));
        }

        [WpfFact]
        public void CopyMultipleRows_JoinsFormatterOutputWithNewlines()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("alpha", 10), ("beta", 20), ("gamma", 30));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            grid.RowFormatter = new ProcessRowFormatter();
            items[0].IsSelected = true;
            items[2].IsSelected = true;

            BetterDataGrid.CopyRowsCommand.Execute(null, grid);

            string expected = string.Join(Environment.NewLine,
                items[0].Process.ToDelimitedString('\t'),
                items[2].Process.ToDelimitedString('\t'));
            Assert.Equal(expected, Clipboard.GetText());
        }

        [WpfFact]
        public void CopyWithoutFormatter_FallsBackToToString()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("alpha", 10), ("beta", 20));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            items[1].IsSelected = true;

            BetterDataGrid.CopyRowsCommand.Execute(null, grid);

            Assert.Equal(items[1].ToString(), Clipboard.GetText());
        }
    }
}
```

- [ ] **Step 5: Run relevant validation**

```bash
dotnet test TaskManager.Tests --filter "FullyQualifiedName~UI_Controls"
```

All eleven tests across the three files pass. If the clipboard assertions fail in a non-interactive session, note that `Clipboard` requires an interactive desktop; run tests from a normal user session.

- [ ] **Step 6: Commit**

```bash
git add TaskManager.Tests/TestSupport/GridTestHost.cs TaskManager.Tests/UI_Controls/
git commit -m "test(ui): cover selection unity, right-click semantics, and clipboard copy via STA behavior tests"
```

---

### Task 5: Formatter unit tests + full verification

**Files:**
- Test: `TaskManager.Tests/UI_Formatters/ProcessRowFormatterTests.cs`

**Interfaces:**
- Consumes: `ProcessRowFormatter` (Task 2), `ProcessItem` (Task 1).
- Produces: nothing downstream — final gate.

- [ ] **Step 1: Add formatter tests**

Create `TaskManager.Tests/UI_Formatters/ProcessRowFormatterTests.cs`:

```csharp
using TaskManager.Domain.Models;
using TaskManager.UI.Formatters;

namespace TaskManager.Tests.UI_Formatters
{
    public class ProcessRowFormatterTests
    {
        [Fact]
        public void ProcessItem_FormatsAsTabDelimited()
        {
            var process = new Process
            {
                Name = "svc",
                Pid = 42,
                Path = @"C:\tools\svc.exe",
                Priority = 8,
                ThreadCount = 3,
                Ppid = 4
            };

            string formatted = new ProcessRowFormatter().Format(new ProcessItem(process));

            Assert.Equal("svc\t42\tC:\\tools\\svc.exe\t8\t3\t4", formatted);
        }

        [Fact]
        public void Null_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, new ProcessRowFormatter().Format(null));
        }

        [Fact]
        public void NonProcessItem_FallsBackToToString()
        {
            var fallback = new PlainFallback();

            Assert.Equal("fallback-text", new ProcessRowFormatter().Format(fallback));
        }

        private sealed class PlainFallback
        {
            public override string ToString() => "fallback-text";
        }
    }
}
```

- [ ] **Step 2: Full-suite verification**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests
```

Every test in the repository passes (old VVM/integration suites plus the 21 new tests: 2 ProcessItem, 5 ColumnVisibility, 5 SelectionUnification, 3 RightClickSemantics, 3 ClipboardCopy, 3 ProcessRowFormatter).

- [ ] **Step 3: Final manual smoke (spec §6.4)**

Launch the app and verify end-to-end: row menu actions (Monitor inert as documented, Copy tab-delimited, SetPriority, Terminate), header menu toggling with Name locked, checkbox/row selection unity, Ctrl-click multi-select, and Polish locale rendering.

- [ ] **Step 4: Commit**

```bash
git add TaskManager.Tests/UI_Formatters/ProcessRowFormatterTests.cs
git commit -m "test(ui): pin ProcessRowFormatter tab-delimited output and fallbacks"
```

---

## Spec Coverage Map (self-review artifact)

| Spec section | Implemented by |
|---|---|
| §3.1 component map | Tasks 2–3 |
| §3.2 responsibilities (routing/menu/copy) | Task 2 |
| §3.3 deletions | Tasks 2 (files, DPs, VM plumbing) |
| §4.1 unified selection | Tasks 1–2 |
| §4.2 right-click table | Task 2 (+ Task 4 tests) |
| §4.3 column visibility + locking | Task 3 |
| §4.4 clipboard copy | Tasks 2, 5 |
| §4.5 preserved limitation | Task 4 `ReplacedItem_StartsUnselected` pins it |
| §5 error handling | Task 2 (COMException, CanExecute, null fallback), Task 3 (lock invariant) |
| §6 testing strategy | Tasks 3–5 |
| §6.4 verification plan | Task 5 steps 2–3 |
