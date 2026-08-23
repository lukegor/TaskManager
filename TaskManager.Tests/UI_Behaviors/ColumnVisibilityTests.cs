using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
