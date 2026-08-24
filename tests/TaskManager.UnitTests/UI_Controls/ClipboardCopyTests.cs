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
            _previousClipboard = GetTextWithRetry();
        }

        public void Dispose()
        {
            if (_previousClipboard.Length > 0)
            {
                SetTextWithRetry(_previousClipboard);
            }
        }

        // the test process shares the live system clipboard with other apps; brief contention is normal
        private static string GetTextWithRetry()
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return Clipboard.GetText();
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 10)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private static void SetTextWithRetry(string text)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 10)
                {
                    Thread.Sleep(50);
                }
            }
        }

        [WpfFact]
        public void NoSelection_CopyCannotExecute()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("a", 1), ("b", 2));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);

            BetterDataGrid.CopyRowsCommand.CanExecute(null, grid).ShouldBeFalse();
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
            GetTextWithRetry().ShouldBe(expected);
        }

        [WpfFact]
        public void CopyWithoutFormatter_FallsBackToToString()
        {
            List<ProcessItem> items = GridTestHost.CreateItems(("alpha", 10), ("beta", 20));
            BetterDataGrid grid = GridTestHost.CreateGrid(items);
            GridTestHost.ApplyUnifiedSelectionStyle(grid);
            items[1].IsSelected = true;

            BetterDataGrid.CopyRowsCommand.Execute(null, grid);

            GetTextWithRetry().ShouldBe(items[1].ToString());
        }
    }
}

