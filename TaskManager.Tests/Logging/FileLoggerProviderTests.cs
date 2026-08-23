using Microsoft.Extensions.Logging;
using TaskManager.Infrastructure.Logging;

namespace TaskManager.Tests
{
    public class FileLoggerProviderTests : IDisposable
    {
        private readonly string _logDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-log-tests-{Guid.NewGuid():N}");

        [Fact]
        public void Write_AppendsFormattedMessageToDailyFile()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            var logger = provider.CreateLogger("Test.Category");

            logger.LogInformation("hello {Name}", "world");

            var file = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(file));
            var content = File.ReadAllText(file);
            Assert.Contains("hello world", content);
            Assert.Contains("[Information]", content);
            Assert.Contains("Test.Category", content);
        }

        [Fact]
        public void Write_IncludesExceptionDetails()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            var logger = provider.CreateLogger("Cat");

            logger.LogError(new InvalidOperationException("boom"), "op failed");

            var content = File.ReadAllText(Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log"));
            Assert.Contains("boom", content);
            Assert.Contains("[Error]", content);
        }

        [Fact]
        public void Constructor_DeletesLogsOlderThanRetention()
        {
            Directory.CreateDirectory(_logDirectory);
            var staleLog = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(staleLog, "old");
            File.SetLastWriteTime(staleLog, DateTime.Now.AddDays(-30));

            using var provider = new FileLoggerProvider(_logDirectory);

            Assert.False(File.Exists(staleLog));
        }

        public void Dispose()
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
    }
}
