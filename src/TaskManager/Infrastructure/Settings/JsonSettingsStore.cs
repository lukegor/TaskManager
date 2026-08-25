using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;

namespace TaskManager.Infrastructure.Settings
{
    /// <summary>
    /// Typed JSON persistence for <see cref="AppSettings"/>.
    /// Atomic writes (temp file + rename), versioned envelope, per-field fallback to
    /// defaults, and quarantining of unparsable files instead of crashing startup.
    /// </summary>
    internal sealed class JsonSettingsStore : ISettingsStore
    {
        private const int CurrentVersion = 1;

        private readonly string _filePath;
        private readonly ILogger<JsonSettingsStore> _logger;

        public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaskManager"), logger)
        {
        }

        public JsonSettingsStore(string directory, ILogger<JsonSettingsStore> logger)
        {
            _filePath = Path.Combine(directory, "settings.json");
            _logger = logger;
        }

        /// <summary>Returns persisted settings; never throws. Missing file yields defaults.</summary>
        public AppSettings Load()
        {
            if (!File.Exists(_filePath))
            {
                return AppSettings.Defaults;
            }

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex)
            {
                // Contract of ISettingsStore.Load: reading must never throw.
                _logger.LogWarning(ex, "Settings file could not be read; using defaults");
                return AppSettings.Defaults;
            }

            SettingsEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsEnvelope);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Settings file is corrupt; quarantining it and using defaults");
                QuarantineCorruptFile();
                return AppSettings.Defaults;
            }

            if (envelope?.Settings is null)
            {
                _logger.LogWarning("Settings file contains no settings payload; using defaults");
                return AppSettings.Defaults;
            }

            if (envelope.Version != CurrentVersion)
            {
                _logger.LogInformation(
                    "Settings file version {Actual} differs from expected {Expected}; attempting best-effort read",
                    envelope.Version, CurrentVersion);
            }

            var settings = Materialize(envelope.Settings);
            _logger.LogDebug("Loaded settings from {File}", _filePath);
            return settings;
        }

        /// <summary>Persists settings atomically; an interrupted write can never truncate the previous file.</summary>
        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var envelope = new SettingsEnvelope(CurrentVersion, new StoredSettings(
                settings.Language,
                settings.ProcessesRefreshFrequency,
                settings.DateTimeFormat));

            var tempPath = _filePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(envelope, SettingsJsonContext.Default.SettingsEnvelope));

                const int maxAttempts = 3;
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        File.Move(tempPath, _filePath, overwrite: true);
                        return;
                    }
                    catch (IOException) when (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
                    }
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private AppSettings Materialize(StoredSettings stored)
        {
            var language = stored.Language is not null && LanguageDictionary.KeysList.Contains(stored.Language)
                ? stored.Language
                : Fallback(nameof(stored.Language), stored.Language, AppSettings.Defaults.Language);

            var storedFrequency = stored.ProcessesRefreshFrequency;
            RefreshFrequencyType frequency;
            if (storedFrequency.HasValue && Enum.IsDefined(storedFrequency.Value))
            {
                frequency = storedFrequency.Value;
            }
            else
            {
                frequency = Fallback(nameof(stored.ProcessesRefreshFrequency), storedFrequency,
                    AppSettings.Defaults.ProcessesRefreshFrequency);
            }

            var dateTimeFormat = stored.DateTimeFormat is not null && AppSettings.AllowedDateTimeFormats.Contains(stored.DateTimeFormat)
                ? stored.DateTimeFormat
                : Fallback(nameof(stored.DateTimeFormat), stored.DateTimeFormat, AppSettings.Defaults.DateTimeFormat);

            return new AppSettings
            {
                Language = language,
                ProcessesRefreshFrequency = frequency,
                DateTimeFormat = dateTimeFormat
            };
        }

        private T Fallback<T>(string fieldName, object? rawValue, T defaultValue)
        {
            _logger.LogWarning(
                "Stored setting '{Field}' has unusable value '{Value}'; falling back to '{Default}'",
                fieldName, rawValue, defaultValue);
            return defaultValue;
        }

        private void QuarantineCorruptFile()
        {
            try
            {
                var quarantinePath = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(_filePath, quarantinePath);
                _logger.LogWarning("Corrupt settings file moved to {QuarantinePath}", quarantinePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not quarantine corrupt settings file at {File}", _filePath);
            }
        }
    }

    internal sealed record SettingsEnvelope(int Version, StoredSettings? Settings);

    /// <summary>Storage shape: nullable fields so partial files degrade per-property instead of failing wholesale.</summary>
    internal sealed record StoredSettings(
        string? Language,
        RefreshFrequencyType? ProcessesRefreshFrequency,
        string? DateTimeFormat);

    [JsonSourceGenerationOptions(WriteIndented = true,
        Converters = [typeof(JsonStringEnumConverter<RefreshFrequencyType>)])]
    [JsonSerializable(typeof(SettingsEnvelope))]
    internal sealed partial class SettingsJsonContext : JsonSerializerContext;
}
