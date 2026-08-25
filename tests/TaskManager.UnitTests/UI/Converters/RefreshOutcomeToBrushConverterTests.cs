using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TaskManager.Presentation;
using TaskManager.UI.Converters;

namespace TaskManager.UnitTests.UI.Converters
{
    public class RefreshOutcomeToBrushConverterTests
    {
        private readonly RefreshOutcomeToBrushConverter _converter = new();

        [Theory]
        [InlineData(RefreshOutcome.Ok, nameof(Brushes.ForestGreen))]
        [InlineData(RefreshOutcome.Failed, nameof(Brushes.IndianRed))]
        [InlineData(RefreshOutcome.Skipped, nameof(Brushes.Gray))]
        public void Convert_MapsOutcomeToExpectedBrush(RefreshOutcome outcome, string expectedBrushName)
        {
            var expected = (SolidColorBrush)typeof(Brushes)
                .GetProperty(expectedBrushName)!
                .GetValue(null)!;

            var brush = _converter.Convert(outcome, typeof(Brush), null, CultureInfo.InvariantCulture);

            ((SolidColorBrush)brush).Color.ShouldBe(expected.Color);
        }

        [Fact]
        public void Convert_NullMapsToNeutralGray()
        {
            var brush = _converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);

            ((SolidColorBrush)brush).Color.ShouldBe(((SolidColorBrush)Brushes.Gray).Color);
        }

        [Fact]
        public void ConvertBack_IsNotSupported()
        {
            Should.Throw<NotSupportedException>(() =>
                _converter.ConvertBack(null, typeof(object), null, CultureInfo.InvariantCulture));
        }
    }
}
