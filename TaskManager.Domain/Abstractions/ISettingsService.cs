using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    public interface ISettingsService : IAppSettings
    {
        abstract void SaveSettings(EditableSettings newSettings);
        abstract void RestoreDefaults();
    }
}
