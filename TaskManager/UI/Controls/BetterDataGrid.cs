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
                Clipboard.SetDataObject(text, copy: true);
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
