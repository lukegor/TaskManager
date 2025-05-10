using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.UI.Controls
{
    internal class ProcessDataGrid : BetterDataGrid
    {
        public static readonly DependencyProperty SetPriorityCommandProperty = DependencyProperty.Register(
            "SetPriorityOptionDependency",
            typeof(ICommand),
            typeof(BetterDataGrid),
            new PropertyMetadata(null));

        public ICommand SetPriorityOptionDependency
        {
            get { return (ICommand)GetValue(SetPriorityCommandProperty); }
            set { SetValue(SetPriorityCommandProperty, value); }
        }

        public static readonly DependencyProperty TerminateCommandProperty = DependencyProperty.Register(
            "TerminateOptionDependency",
            typeof(ICommand),
            typeof(BetterDataGrid),
            new PropertyMetadata(null));

        public ICommand TerminateOptionDependency
        {
            get { return (ICommand)GetValue(TerminateCommandProperty); }
            set { SetValue(TerminateCommandProperty, value); }
        }

        protected override void ShowRowContextMenu(Point position, DataGridRow clickedRow)
        {
            ContextMenu contextMenu = new ContextMenu();

            var monitorMenuItem = new MenuItem
            {
                Header = Strings.Monitor,
                // keep info which row was clicked in Tag
                Tag = clickedRow
            };
            monitorMenuItem.Click += base.OnMonitorMenuItemClick;
            contextMenu.Items.Add(monitorMenuItem);

            var copyMenuItem = new MenuItem
            {
                Header = Strings.CopyToClipboard,
                Tag = clickedRow
            };
            copyMenuItem.Click += OnCopyMenuItemClick;
            contextMenu.Items.Add(copyMenuItem);

            var setPriorityMenuItem = new MenuItem
            {
                Header = Strings.SetPriority,
                Tag = clickedRow,
                Command = SetPriorityOptionDependency,
                CommandParameter = clickedRow
            };
            contextMenu.Items.Add(setPriorityMenuItem);

            var terminateMenuItem = new MenuItem
            {
                Header = Strings.Terminate,
                Tag = clickedRow,
                Command = TerminateOptionDependency,
                CommandParameter = clickedRow
            };
            contextMenu.Items.Add(terminateMenuItem);

            contextMenu.IsOpen = true;
        }
    }
}
