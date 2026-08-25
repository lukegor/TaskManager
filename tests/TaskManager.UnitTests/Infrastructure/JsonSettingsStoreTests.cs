using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Infrastructure.Settings;
using TaskManager.Domain.Primitives;

namespace TaskManager.UnitTests.Infrastructure
{
    /// <summary>
    /// Persistence contract of JsonSettingsStore: round-trips typed values,
    /// survives partial and corrupt files without throwing, writes atomically.
    /// </summary>
    public sealed class JsonSettingsStoreTests : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), $"tm-settings-tests-{Guid.NewGuid():N}");

        private JsonSettingsStore CreateStore() =>
            new(_directory, NullLogger<JsonSettingsStore>.Instance);

        private void WriteRaw(string json)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, json);
        }

        private string FilePath => Path.Combine(_directory, "settings.json");

        [Fact]
        public void Load_WhenFileMissing_ReturnsDefaultsWithoutCreatingFile()
        {
            var settings = CreateStore().Load();

            settings.ShouldBe(AppSettings.Defaults);
            File.Exists(FilePath).ShouldBeFalse();
        }

        [Fact]
        public void SaveThenLoad_RoundTripsAllValues()
        {
            var original = new AppSettings
            {
                Language = "polski",
                ProcessesRefreshFrequency = RefreshFrequencyType.Paused,
                DateTimeFormat = "MM_dd_yyyy--HH_mm_ss"
            };

            CreateStore().Save(original);
            var loaded = CreateStore().Load();

            loaded.ShouldBe(original);
        }

        [Fact]
        public void Save_PersistsEnumAsString()
        {
            CreateStore().Save(AppSettings.Defaults with { ProcessesRefreshFrequency = RefreshFrequencyType.High });

            File.ReadAllText(FilePath).ShouldContain("\"High\"");
        }

        [Fact]
        public void Save_LeavesNoTempFileBehind()
        {
            CreateStore().Save(AppSettings.Defaults);

            Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
        }

        [Fact]
        public void Load_WithCorruptJson_QuarantinesFileAndReturnsDefaults()
        {
            WriteRaw("{ not valid json !!!");

            var settings = CreateStore().Load();

            settings.ShouldBe(AppSettings.Defaults);
            File.Exists(FilePath).ShouldBeFalse();
            Directory.GetFiles(_directory, "settings.json.corrupt-*").ShouldHaveSingleItem();
        }

        [Fact]
        public void Load_WithMissingFields_FallsBackPerProperty()
        {
            WriteRaw(
                """
                { "Version": 1, "Settings": { "Language": "polski" } }
                """);

            var settings = CreateStore().Load();

            settings.Language.ShouldBe("polski");
            settings.ProcessesRefreshFrequency.ShouldBe(AppSettings.Defaults.ProcessesRefreshFrequency);
            settings.DateTimeFormat.ShouldBe(AppSettings.Defaults.DateTimeFormat);
        }

        [Fact]
        public void Load_WithInvalidFieldValues_FallsBackPerProperty()
        {
            WriteRaw(
                """
                { "Version": 1, "Settings": { "Language": "deutsch", "ProcessesRefreshFrequency": "Paused", "DateTimeFormat": "yyyy-MM-dd" } }
                """);

            var settings = CreateStore().Load();

            settings.Language.ShouldBe(AppSettings.Defaults.Language);
            settings.ProcessesRefreshFrequency.ShouldBe(RefreshFrequencyType.Paused);
            settings.DateTimeFormat.ShouldBe(AppSettings.Defaults.DateTimeFormat);
        }

        [Fact]
        public void Load_WithNullPayload_ReturnsDefaults()
        {
            WriteRaw("""{ "Version": 1, "Settings": null }""");

            CreateStore().Load().ShouldBe(AppSettings.Defaults);
        }

        [Fact]
        public void Save_IntoMissingDirectory_CreatesDirectoryAndSucceeds()
        {
            var nested = Path.Combine(_directory, "nested", "deeper");
            var store = new JsonSettingsStore(nested, NullLogger<JsonSettingsStore>.Instance);

            store.Save(AppSettings.Defaults);

            new JsonSettingsStore(nested, NullLogger<JsonSettingsStore>.Instance)
                .Load().ShouldBe(AppSettings.Defaults);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
