using TaskManager.Domain.Abstractions;

namespace TaskManager.Domain.Models.Mappers
{
    public static class SettingsMapper
    {
        public static EditableSettings ToEditables(this ISettingsService settings)
        {
            return new EditableSettings()
            {
                Language = settings.Language,
                ProcessesRefreshFrequency = settings.RefreshFrequency,
                DateTimeFormat = settings.DateTimeFormat
            };
        }
    }
}
