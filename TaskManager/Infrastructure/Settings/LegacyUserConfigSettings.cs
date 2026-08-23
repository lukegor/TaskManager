using System.Configuration;

namespace TaskManager.Infrastructure.Settings
{
    /// <summary>
    /// Read-only access to the pre-migration ApplicationSettingsBase store.
    /// The group name pins this shim to the exact section written by the former
    /// TaskManager.Properties.Settings designer type, so the framework resolves
    /// the same user.config without keeping that file alive.
    /// Used exclusively by <see cref="LegacySettingsMigrator"/>; never persisted to.
    /// </summary>
    [SettingsGroupName("TaskManager.Properties.Settings")]
    internal sealed class LegacyUserConfigSettings : ApplicationSettingsBase
    {
        [UserScopedSetting]
        [DefaultSettingValue("English")]
        public string LanguageVersion => (string)this[nameof(LanguageVersion)];

        [UserScopedSetting]
        [DefaultSettingValue("1")]
        public string RefreshFrequency => (string)this[nameof(RefreshFrequency)];

        [UserScopedSetting]
        [DefaultSettingValue("yyyy_MM_dd--HH_mm_ss")]
        public string DateTimeFormat => (string)this[nameof(DateTimeFormat)];
    }
}
