using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    public interface ISettingsService : IAppSettings
    {
        void SaveSettings(EditableSettings newSettings);
        void RestoreDefaults();
    }
}
