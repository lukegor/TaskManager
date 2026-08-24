using System.Globalization;
using System.Windows.Data;
using TaskManager.Domain.Primitives;
using TaskManager.UI.Localization;

namespace TaskManager.Utility.Converters
{
    public class ProcessRefreshFrequenceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            RefreshFrequencyType processesRefreshFrequencyType = (RefreshFrequencyType)value;
            return RefreshFrequencyTypeHelper.MapEnumToLocalString(processesRefreshFrequencyType);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            string localizedProcessesRefreshFrequencyType = value.ToString()!;
            return RefreshFrequencyTypeHelper.MapLocalStringToEnum(localizedProcessesRefreshFrequencyType);
        }
    }
}
