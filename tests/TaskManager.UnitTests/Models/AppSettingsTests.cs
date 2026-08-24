using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Tests.Models
{
    public class AppSettingsTests
    {
        [Fact]
        public void Defaults_AreValid()
        {
            AppSettings.TryValidate(AppSettings.Defaults, out var error).ShouldBeTrue();
            error.ShouldBeNull();
        }

        [Fact]
        public void TryValidate_NullInstance_ReturnsFalseWithError()
        {
            AppSettings.TryValidate(null!, out var error).ShouldBeFalse();
            error.ShouldNotBeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("deutsch")]
        [InlineData("English ")]
        public void TryValidate_UnknownLanguage_ReturnsFalse(string language)
        {
            var settings = AppSettings.Defaults with { Language = language };

            AppSettings.TryValidate(settings, out var error).ShouldBeFalse();
            error.ShouldContain("language");
        }

        [Fact]
        public void TryValidate_UndefinedFrequency_ReturnsFalse()
        {
            var settings = AppSettings.Defaults with
            {
                ProcessesRefreshFrequency = (RefreshFrequencyType)999
            };

            AppSettings.TryValidate(settings, out var error).ShouldBeFalse();
            error.ShouldContain("frequency");
        }

        [Theory]
        [InlineData("")]
        [InlineData("yyyy-MM-dd")]
        [InlineData("yyyy_MM_dd--HH_mm_ss ")]
        public void TryValidate_UnsupportedDateTimeFormat_ReturnsFalse(string format)
        {
            var settings = AppSettings.Defaults with { DateTimeFormat = format };

            AppSettings.TryValidate(settings, out var error).ShouldBeFalse();
            error.ShouldContain("format");
        }

        [Fact]
        public void AllowedDateTimeFormats_ContainDefaultsFormat()
        {
            AppSettings.AllowedDateTimeFormats.ShouldContain(AppSettings.Defaults.DateTimeFormat);
        }
    }
}
