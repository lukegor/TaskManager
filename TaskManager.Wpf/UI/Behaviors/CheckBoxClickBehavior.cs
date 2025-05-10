using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.UI.Behaviors
{
	/// <summary>
	/// CheckBox Behavior which is effectively used to fully synchronize CheckBoxes in BetterDataGrid with rows selection
	/// Effectively synchronizes both visual effects and stored data
	/// </summary>
	internal class CheckBoxClickBehavior : Behavior<CheckBox>
	{
		protected override void OnAttached()
		{
			base.OnAttached();
			AssociatedObject.PreviewMouseDown += OnCheckBoxPreviewMouseDown;
		}

		protected override void OnDetaching()
		{
			base.OnDetaching();
			AssociatedObject.PreviewMouseDown -= OnCheckBoxPreviewMouseDown;
		}

		private void OnCheckBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
		{
			// prevent default behavior
			e.Handled = true;

			// manually update DataGridRow selection & ProcessItem state selection
			if (AssociatedObject.DataContext is ProcessItem processItem)
			{
				var dataGridRow = VisualTreeUtilityHelper.FindVisualParent<DataGridRow>(AssociatedObject);
				if (dataGridRow != null)
				{
					bool isChecked = !AssociatedObject.IsChecked.GetValueOrDefault();
					dataGridRow.IsSelected = isChecked;
					processItem.IsSelected = isChecked;

					// update checkbox
					AssociatedObject.IsChecked = isChecked;
				}
			}
		}
	}
}
