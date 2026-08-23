using System.Collections.ObjectModel;
using System.Windows.Controls;
using TaskManager.Domain.Models;
using TaskManager.Tests.TestSupport;
using TaskManager.UI.Controls;

namespace TaskManager.Tests.UI_Controls
{
    // Note on scope: ctrl-click multi-select mechanics are framework-owned; the app-owned
    // contract is the TwoWay binding, exercised identically by programmatic IsSelected writes
    // on either side. Checkbox ticks hit the very same property, so they are covered transitively.
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
