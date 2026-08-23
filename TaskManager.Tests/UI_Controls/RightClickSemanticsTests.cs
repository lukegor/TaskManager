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
