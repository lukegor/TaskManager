using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
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
            // Volume variant: a larger buffered backlog must fully survive the
            // bounded-wait shutdown flush. Mid-life visibility is intentionally not
            // asserted (flush-on-idle makes it scheduling-dependent); the no-sync-I/O
            // guarantee is structural (producer only enqueues).
            using var provider = new FileLoggerProvider(_logDirectory);
            var logger = provider.CreateLogger("Cat");

            for (var i = 0; i < 250; i++)
            {
                logger.LogInformation("backlog entry {Index}", i);
            }

            await provider.DisposeAsync();

            var content = ReadTodayLog();
            content.ShouldContain("backlog entry 0");
            content.ShouldContain("backlog entry 249");
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

        [Fact]
        public async Task Rollover_CreatesNewDailyFile_AndRunsRetention()
        {
            // Offset taken from the local zone so the wall-clock date really is
            // Aug 25 everywhere: Append names files by Timestamp.LocalDateTime.
            var time = new FakeTimeProvider(new DateTimeOffset(
                2026, 8, 25, 23, 59, 50, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 25))));
            using var provider = new FileLoggerProvider(_logDirectory, time);
            var logger = provider.CreateLogger("Cat");
            logger.LogInformation("before midnight");

            // Stale file created AFTER construction so its deletion can only be the work
            // of the rollover-time retention pass, not the constructor's. Retention is
            // last-write-based, so the mtime must be backdated to qualify as expired.
            var stale = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(stale, "old");
            File.SetLastWriteTime(stale, time.GetLocalNow().LocalDateTime.AddDays(-30));

            time.Advance(TimeSpan.FromSeconds(20)); // crosses midnight -> 2026-08-26
            logger.LogInformation("after midnight");
            await provider.DisposeAsync();

            File.Exists(Path.Combine(_logDirectory, "tm-20260825.log")).ShouldBeTrue();
            var nextDay = Path.Combine(_logDirectory, "tm-20260826.log");
            File.Exists(nextDay).ShouldBeTrue();
            File.ReadAllText(nextDay).ShouldContain("after midnight");
            File.Exists(stale).ShouldBeFalse();
        }

        [Fact]
        public async Task Overflow_ReportsExactlyOneNotice_PerEpisode()
        {
            // Windows file-sharing makes mid-drain reads impossible (the drain's
            // StreamWriter denies other readers), so each episode is observed the
            // same way as every test here: DisposeAsync-centered, after the flush.
            const int capacity = 64;

            using (var provider = new FileLoggerProvider(_logDirectory, TimeProvider.System, capacity))
            {
                Burst(provider.CreateLogger("Cat"), capacity * 4);
                await provider.DisposeAsync(); // closes the stream so the file can be read whole
                await WaitForAsync(() => ReadTodayLog().Contains("log buffer overflowed"));
                CountOccurrences(ReadTodayLog(), "log buffer overflowed").ShouldBe(1);
            }

            // Second episode on a recovered logger: exactly one more notice.
            using (var provider = new FileLoggerProvider(_logDirectory, TimeProvider.System, capacity))
            {
                Burst(provider.CreateLogger("Cat"), capacity * 4);
                await provider.DisposeAsync();
            }

            var content = ReadTodayLog();
            CountOccurrences(content, "log buffer overflowed").ShouldBe(2);
            content.ShouldNotContain("burst 0");   // oldest entries were the ones dropped
            content.ShouldContain($"burst {(capacity * 4) - 1}"); // newest survived
        }

        [Fact]
        public async Task Drain_SurvivesStreamFailure()
        {
            Directory.CreateDirectory(_logDirectory);
            // Portability note: FileShare.None is Windows-enforced; on Linux this lock
            // is advisory/no-op and the test passes while exercising less.
            using var lockHandle = File.Open(
                Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log"),
                FileMode.OpenOrCreate, FileAccess.Read, FileShare.None); // denies any writer

            using var provider = new FileLoggerProvider(_logDirectory);
            provider.CreateLogger("Cat").LogInformation("never lands");

            await provider.DisposeAsync(); // must not throw despite the unwritable target
        }

        private static void Burst(Microsoft.Extensions.Logging.ILogger logger, int count)
        {
            for (var i = 0; i < count; i++)
            {
                logger.LogInformation("burst {Index}", i);
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            if (!condition())
            {
                throw new TimeoutException("logging test condition was not met within 5 s");
            }
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private string ReadTodayLog()
        {
            var path = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty; // mid-drain poll: writer holds the file; next tick retries
            }
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
