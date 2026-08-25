using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TaskManager.UnitTests.AppStartup
{
    /// <summary>Pure parse contract of the TASKMANAGER_LOGLEVEL override.</summary>
    public class LogFloorTests
    {
        [Theory]
        [InlineData("Information", LogLevel.Information)]
        [InlineData("debug", LogLevel.Debug)]
        [InlineData("Warning", LogLevel.Warning)]
        public void TryResolve_KnownNames_ParseCaseInsensitively(string raw, LogLevel expected)
        {
            App.TryResolveMinimumLogLevel(raw, out var level).ShouldBeTrue();
            level.ShouldBe(expected);
        }

        [Theory]
        [InlineData("verbose")]
        [InlineData("")]
        [InlineData(null)]
        public void TryResolve_UnknownOrMissing_FailsWithoutThrowing(string? raw)
        {
            App.TryResolveMinimumLogLevel(raw, out var level).ShouldBeFalse();
            level.ShouldBe(default(LogLevel)); // None — callers must not use it
        }
    }
}
