using Task_Manager.Models;
using Task_Manager.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Task_Manager.UI.Controls
{
    internal class BetterDataGrid : BaseBetterDataGrid
    {
        protected override void ShowRowContextMenu(Point position, DataGridRow clickedRow)
        {
            ContextMenu contextMenu = new ContextMenu();

            var copyMenuItem = new MenuItem
            {
                Header = Strings.CopyToClipboard,
                Tag = clickedRow
            };
            copyMenuItem.Click += OnCopyMenuItemClick;
            contextMenu.Items.Add(copyMenuItem);

            contextMenu.IsOpen = true;
        }

        protected override void ShowColumnHeaderContextMenu(Point position)
        {
            ContextMenu contextMenu = new ContextMenu();

            foreach (var column in Columns)
            {
                var menuItem = new MenuItem
                {
                    Header = column.Header.ToString(),
                    IsCheckable = true,
                    IsChecked = column.Visibility == Visibility.Visible,
                    Tag = column
                };

                if (column.Header.ToString() == Strings.Name)
                {
                    menuItem.IsEnabled = false;
                }
                // bind visibility of the column to the checkbox
                menuItem.Click += base.OnMenuItemClick;

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.IsOpen = true;
        }

        protected void OnCopyMenuItemClick(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var clickedRow = menuItem?.Tag as DataGridRow;
            if (clickedRow != null)
            {
                // Get the row's bound data item
                var rowData = clickedRow.DataContext;
                string rowText = FormatRowData(rowData);

                Clipboard.SetText(rowText);
            }
        }

        private string FormatRowData(object rowData)
        {
            if (rowData is ProcessItem process)
            {
                return process.Process.ToDelimitedString('\t');
            }

            // fallback
            return rowData?.ToString() ?? string.Empty;
        }
    }
}
