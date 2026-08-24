using System.Collections.ObjectModel;
using System.Windows.Controls;
using TaskManager.Domain.Models;
using TaskManager.UnitTests.TestSupport;
using TaskManager.UI.Controls;

namespace TaskManager.UnitTests.UI.Controls
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

            items[0].IsSelected.ShouldBeTrue();
            grid.SelectedItems.Cast<ProcessItem>().ShouldContain(items[0]);
        }

        [WpfFact]
        public void MultipleRowsSelected_AllModelsReflectIt()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);

            GridTestHost.GetRow(grid, 0).IsSelected = true;
            GridTestHost.GetRow(grid, 2).IsSelected = true;

            items[0].IsSelected.ShouldBeTrue();
            items[2].IsSelected.ShouldBeTrue();
            items[1].IsSelected.ShouldBeFalse();
            grid.SelectedItems.Count.ShouldBe(2);
        }

        [WpfFact]
        public void ModelChange_UpdatesRowHighlight()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);

            items[1].IsSelected = true;

            GridTestHost.GetRow(grid, 1).IsSelected.ShouldBeTrue();
        }

        [WpfFact]
        public void ModelDeselect_ClearsRowHighlight()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            items[1].IsSelected.ShouldBeTrue();

            items[1].IsSelected = false;

            GridTestHost.GetRow(grid, 1).IsSelected.ShouldBeFalse();
        }

        [WpfFact]
        public void ReplacedItem_StartsUnselected()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2), ("c", 3));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            GridTestHost.GetRow(grid, 1).IsSelected = true;
            items[1].IsSelected.ShouldBeTrue();

            var collection = (ObservableCollection<ProcessItem>)grid.ItemsSource;
            collection[1] = new ProcessItem(new Process { Name = "replacement", Pid = 99, Path = string.Empty });
            grid.UpdateLayout();

            collection[1].IsSelected.ShouldBeFalse();
            GridTestHost.GetRow(grid, 1).IsSelected.ShouldBeFalse();
        }
    }
}


