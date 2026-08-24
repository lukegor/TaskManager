using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using TaskManager.Domain.Primitives;
using TaskManager.UI.Localization;

namespace TaskManager.Utility.Converters
{
    public class ProcessPriorityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            ProcessPriorityClass priorityType = (ProcessPriorityClass)value;
            return PriorityTypeHelper.MapEnumToLocalString(priorityType);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            string localizedPriorityType = value.ToString()!;
            return PriorityTypeHelper.MapLocalStringToEnum(localizedPriorityType);
        }
    }
}
