using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;

namespace Task_Manager.Utility
{
	internal static class VisualTreeUtilityHelper
	{
		public static TDependency FindVisualParent<TDependency>(DependencyObject child) where TDependency : DependencyObject
		{
			while (child != null)
			{
				if (child is TDependency parent)
				{
					return parent;
				}
				child = VisualTreeHelper.GetParent(child);
			}
			return null;
		}

		public static TVisual GetVisualChild<TVisual>(Visual parent) where TVisual : Visual
		{
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
			{
				var child = VisualTreeHelper.GetChild(parent, i) as Visual;
				if (child != null)
				{
					if (child is TVisual t)
					{
						return t;
					}
					var result = GetVisualChild<TVisual>(child);
					if (result != null)
					{
						return result;
					}
				}
			}
			return null;
		}
	}
}
