using Microsoft.Extensions.Logging;
using TaskManager.Infrastructure.Logging;

namespace TaskManager.UnitTests
{
    /// <summary>
    /// All assertions are DisposeAsync-centered by design: the producer only enqueues,
    /// so durable content is observable after the bounded-wait shutdown flush.
    /// </summary>
    public class FileLoggerProviderTests : IDisposable
    {
        private readonly string _logDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-log-tests-{Guid.NewGuid():N}");

        [Fact]
        public async Task Write_AppendsFormattedMessageToDailyFile_OnDispose()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Test.Category").LogInformation("hello {Name}", "world");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("hello world");
            content.ShouldContain("[Information]");
            content.ShouldContain("Test.Category");
        }

        [Fact]
        public async Task Write_IncludesExceptionDetails_OnDispose()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogError(new InvalidOperationException("boom"), "op failed");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("op failed");
            content.ShouldContain("[Error]");
            content.ShouldContain("boom");
        }

        [Fact]
        public async Task Write_PersistsThroughShutdownFlush()
        {
            // Mid-life visibility is intentionally not asserted: flush-on-idle makes it
            // scheduling-dependent. The no-sync-I/O guarantee is structural (producer
            // only enqueues), proven here only through the shutdown flush contract.
            var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogInformation("shutdown flush marker");

            await provider.DisposeAsync();

            ReadTodayLog().ShouldContain("shutdown flush marker");
        }

        [Fact]
        public async Task Dispose_FlushesPendingEntries_FromMultipleCategories()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Category.A").LogWarning("first");
            provider.CreateLogger("Category.B").LogWarning("second");

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("first");
            content.ShouldContain("second");
        }

        [Fact]
        public async Task Dispose_IsIdempotent()
        {
            var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogInformation("once");

            await provider.DisposeAsync();
            await provider.DisposeAsync(); // must not throw
            provider.Dispose();            // sync path over disposed provider: also fine
        }

        [Fact]
        public void Constructor_DeletesLogsOlderThanRetention()
        {
            Directory.CreateDirectory(_logDirectory);
            var staleLog = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(staleLog, "old");
            File.SetLastWriteTime(staleLog, DateTime.Now.AddDays(-30));

            using var provider = new FileLoggerProvider(_logDirectory);

            File.Exists(staleLog).ShouldBeFalse();
        }

        private string ReadTodayLog()
        {
            var path = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
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
