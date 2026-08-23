using Microsoft.Extensions.Logging;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Infrastructure.Settings
{
    /// <summary>
    /// One-time import of the legacy ApplicationSettingsBase store into the JSON store.
    /// Raw legacy values are returned unparsed; validation and per-field fallback are
    /// delegated to the JSON store's materialization, so there is exactly one rule set.
    /// </summary>
    internal static class LegacySettingsMigrator
    {
        public static StoredSettings? TryRead(ILogger logger)
        {
            try
            {
                var legacy = new LegacyUserConfigSettings();
                var language = legacy.LanguageVersion;
                var frequency = legacy.RefreshFrequency;
                var dateTimeFormat = legacy.DateTimeFormat;

                if (string.IsNullOrEmpty(language) && string.IsNullOrEmpty(frequency) && string.IsNullOrEmpty(dateTimeFormat))
                {
                    return null;
                }

                return new StoredSettings(language, ParseFrequency(frequency), dateTimeFormat);
            }
            catch (Exception ex)
            {
                // Migration is best-effort by definition; defaults apply when it fails.
                logger.LogWarning(ex, "Legacy settings migration failed; continuing with defaults");
                return null;
            }
        }

        internal static RefreshFrequencyType? ParseFrequency(string? raw)
        {
            return int.TryParse(raw, out var value) && Enum.IsDefined((RefreshFrequencyType)value)
                ? (RefreshFrequencyType)value
                : null;
        }
    }
}
