using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using System.Collections;
using TaskManager.Utility.Utility;

namespace TaskManager.UI.Controls
{
	internal abstract class BaseBetterDataGrid : DataGrid
	{
		#region DependencyProperties
		public static readonly DependencyProperty CommandProperty =
			DependencyProperty.Register(nameof(OnMonitorMenuItemClickedCommand), typeof(ICommand), typeof(BaseBetterDataGrid));
		#endregion

		// commands
		public ICommand OnMonitorMenuItemClickedCommand
		{
			get => (ICommand)GetValue(CommandProperty);
			set => SetValue(CommandProperty, value);
		}

		// events
		public event RoutedEventHandler MonitorMenuItemClick;

		protected BaseBetterDataGrid()
		{
			MouseRightButtonDown += OnMouseRightButtonDown;
		}

		private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			var clickedElement = e.OriginalSource as DependencyObject;

			DataGridRow clickedRow = VisualTreeUtilityHelper.FindVisualParent<DataGridRow>(clickedElement);
			if (clickedRow != null)
			{
                this.SelectedItems.Clear();
                clickedRow.IsSelected = true;

                // right-click on a data row
                ShowRowContextMenu(e.GetPosition(this), clickedRow);
			}
			else if (IsPointInColumnHeaders(e.GetPosition(this)))
			{
				// right-click on column headers
				ShowColumnHeaderContextMenu(e.GetPosition(this));
			}
		}

		#region Rows_Logic
		protected abstract void ShowRowContextMenu(Point position, DataGridRow clickedRow);

		protected void OnMonitorMenuItemClick(object sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			var clickedRow = menuItem?.Tag as DataGridRow;

			if (clickedRow != null)
			{
				if (OnMonitorMenuItemClickedCommand != null && OnMonitorMenuItemClickedCommand.CanExecute(null))
				{
					OnMonitorMenuItemClickedCommand.Execute(null);
				}
			}
		}
		#endregion

		#region ColumnHeaders_Logic
		protected virtual void ShowColumnHeaderContextMenu(Point position)
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

				// bind visibility of the column to the checkbox
				menuItem.Click += OnMenuItemClick;

				contextMenu.Items.Add(menuItem);
			}

			contextMenu.IsOpen = true;
		}

		protected void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem menuItem)
			{
				// get column from Tag
				var column = menuItem.Tag as DataGridColumn;
				if (column != null)
				{
					column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
				}
			}
		}
		#endregion

		#region Utility
		private bool IsPointInColumnHeaders(Point point)
		{
			var headersPresenter = VisualTreeUtilityHelper.GetVisualChild<DataGridColumnHeadersPresenter>(this);
			if (headersPresenter != null)
			{
				var headersRect = VisualTreeHelper.GetDescendantBounds(headersPresenter);
				return headersRect.Contains(point);
			}
			return false;
		}

		#endregion

		#region OnSelectionChanged
		protected override void OnSelectionChanged(SelectionChangedEventArgs e)
		{
			base.OnSelectionChanged(e);
		}

		private static void OnSelectedItemsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var dataGrid = (BaseBetterDataGrid)d;
			dataGrid.OnSelectedItemsChanged((IList)e.OldValue, (IList)e.NewValue);
		}

		protected virtual void OnSelectedItemsChanged(IList oldSelectedItems, IList newSelectedItems)
		{

		}
		#endregion
	}
}
