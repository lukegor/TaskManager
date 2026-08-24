using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TaskManager.UI.Behaviors;

namespace TaskManager.UnitTests.UI.Behaviors
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

            menu.Items.Count.ShouldBe(3);
            foreach (MenuItem item in menu.Items.Cast<MenuItem>())
            {
                item.IsCheckable.ShouldBeTrue();
            }
        }

        [WpfFact]
        public void BuildHeaderMenu_LockedColumn_DisabledAndCheckedWhenVisible()
        {
            DataGrid grid = CreateGridWithColumns();

            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);

            MenuItem lockedItem = (MenuItem)menu.Items[0];
            lockedItem.IsEnabled.ShouldBeFalse();
            lockedItem.IsChecked.ShouldBeTrue();
        }

        [WpfFact]
        public void Click_VisibleUnlockedItem_HidesItsColumn()
        {
            DataGrid grid = CreateGridWithColumns();
            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);
            var pidItem = (MenuItem)menu.Items[1];

            pidItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            grid.Columns[1].Visibility.ShouldBe(Visibility.Collapsed);
            pidItem.IsChecked.ShouldBeFalse();
        }

        [WpfFact]
        public void Click_HiddenColumnItem_ShowsColumnAgain()
        {
            DataGrid grid = CreateGridWithColumns();
            grid.Columns[2].Visibility = Visibility.Collapsed;
            ContextMenu menu = GridColumnVisibility.BuildHeaderMenu(grid);
            var priorityItem = (MenuItem)menu.Items[2];
            priorityItem.IsChecked.ShouldBeFalse();

            priorityItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            grid.Columns[2].Visibility.ShouldBe(Visibility.Visible);
        }

        [WpfFact]
        public void ShowHeaderMenu_OpensAnchoredToGrid()
        {
            DataGrid grid = CreateGridWithColumns();

            ContextMenu menu = GridColumnVisibility.ShowHeaderMenu(grid);

            menu.IsOpen.ShouldBeTrue();
            menu.PlacementTarget.ShouldBeSameAs(grid);
            menu.Placement.ShouldBe(PlacementMode.MousePoint);
            menu.IsOpen = false;
        }
    }
}

