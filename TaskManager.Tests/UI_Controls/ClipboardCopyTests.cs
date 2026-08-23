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
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
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
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            items[1].IsSelected = true;

            BetterDataGrid.CopyRowsCommand.Execute(null, grid);

            Assert.Equal(items[1].ToString(), Clipboard.GetText());
        }
    }
}
