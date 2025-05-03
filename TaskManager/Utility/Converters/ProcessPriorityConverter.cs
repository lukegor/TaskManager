using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Task_Manager.Utility.Converters
{
    public class ProcessPriorityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            ProcessPriorityClass priorityType = (ProcessPriorityClass)value;
            return PriorityTypeHelper.MapEnumToLocalString(priorityType);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            string localizedPriorityType = value.ToString();
            return PriorityTypeHelper.MapLocalStringToEnum(localizedPriorityType);
        }
    }
}
