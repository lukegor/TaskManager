using System.Diagnostics.CodeAnalysis;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Immutable snapshot of all user settings. The single transfer type between
    /// persistence, the settings service, and consumers; value equality enables
    /// cheap change detection (e.g. comparing language before/after a dialog).
    /// </summary>
    public sealed record AppSettings
    {
        public required string Language { get; init; }

        public required RefreshFrequencyType ProcessesRefreshFrequency { get; init; }

        public required string DateTimeFormat { get; init; }

        public static IReadOnlyList<string> AllowedDateTimeFormats { get; } =
        [
            "yyyy_MM_dd--HH_mm_ss",
            "dd_MM_yyyy--HH_mm_ss",
            "MM_dd_yyyy--HH_mm_ss"
        ];

        public static AppSettings Defaults { get; } = new()
        {
            Language = "English",
            ProcessesRefreshFrequency = RefreshFrequencyType.Low,
            DateTimeFormat = AllowedDateTimeFormats[0]
        };

        /// <summary>
        /// Single validation gate for every entry point (deserialization, update).
        /// Returns <see langword="true"/> when all values are usable as-is.
        /// </summary>
        public static bool TryValidate(AppSettings settings, [NotNullWhen(false)] out string? error)
        {
            if (settings is null)
            {
                error = "settings instance is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.Language) || !LanguageDictionary.KeysList.Contains(settings.Language))
            {
                error = $"unknown language '{settings.Language}'";
                return false;
            }

            if (!Enum.IsDefined(settings.ProcessesRefreshFrequency))
            {
                error = $"undefined refresh frequency '{settings.ProcessesRefreshFrequency}'";
                return false;
            }

            if (!AllowedDateTimeFormats.Contains(settings.DateTimeFormat))
            {
                error = $"unsupported date-time format '{settings.DateTimeFormat}'";
                return false;
            }

            error = null;
            return true;
        }
    }
}
