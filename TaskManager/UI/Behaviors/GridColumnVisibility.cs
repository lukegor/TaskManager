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
                // state is derived from the column itself (not the item's transient check state)
                // and IsChecked is set explicitly, so behavior is identical for real clicks
                // and programmatically raised Click events
                menuItem.Click += (_, _) =>
                {
                    bool isVisible = column.Visibility == Visibility.Visible;
                    column.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                    menuItem.IsChecked = !isVisible;
                };

                menu.Items.Add(menuItem);
            }

            return menu;
        }
    }
}
