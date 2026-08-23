using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TaskManager.Domain.Models;
using TaskManager.UI.Controls;

namespace TaskManager.Tests.TestSupport
{
    /// <summary>
    /// Builds fully laid-out BetterDataGrid instances off-screen so row containers exist
    /// and real input events can be raised against them. No window is ever shown.
    /// </summary>
    internal static class GridTestHost
    {
        private static readonly Size LayoutSize = new(400, 300);

        public static List<ProcessItem> CreateItems(params (string Name, int Pid)[] processes)
        {
            return processes
                .Select(p => new ProcessItem(new Process { Name = p.Name, Pid = p.Pid }))
                .ToList();
        }

        public static BetterDataGrid CreateGrid(IList<ProcessItem> items)
        {
            var grid = new BetterDataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new ObservableCollection<ProcessItem>(items)
            };

            // forcing measure/arrange realizes DataGridRow containers without showing anything
            var container = new Border { Child = grid };
            container.Measure(LayoutSize);
            container.Arrange(new Rect(LayoutSize));
            grid.UpdateLayout();

            return grid;
        }

        /// <summary>Mirrors the production ItemContainerStyle declared in MainWindow.xaml.</summary>
        public static void ApplyUnifiedSelectionStyle(BetterDataGrid grid)
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(DataGridRow.IsSelectedProperty,
                new Binding(nameof(ProcessItem.IsSelected)) { Mode = BindingMode.TwoWay }));
            grid.ItemContainerStyle = style;
            grid.UpdateLayout();
        }

        public static DataGridRow GetRow(BetterDataGrid grid, int index)
        {
            return (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index)!;
        }

        public static void RaiseRightClick(FrameworkElement target)
        {
            target.RaiseEvent(new MouseButtonEventArgs(
                InputManager.Current.PrimaryMouseDevice,
                Environment.TickCount,
                MouseButton.Right)
            {
                RoutedEvent = Mouse.MouseDownEvent
            });
        }
    }
}
