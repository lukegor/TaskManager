using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TaskManager.Presentation;

namespace TaskManager.UI.Converters
{
    /// <summary>Maps a refresh outcome to its status-dot brush; null (no sample yet) maps to neutral gray.</summary>
    public sealed class RefreshOutcomeToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value switch
            {
                RefreshOutcome.Ok => Brushes.ForestGreen,
                RefreshOutcome.Failed => Brushes.IndianRed,
                _ => Brushes.Gray, // Skipped or no sample yet
            };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
