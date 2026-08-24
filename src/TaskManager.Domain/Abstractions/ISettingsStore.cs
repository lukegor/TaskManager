using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    /// <summary>Persistence seam for <see cref="ISettingsService"/> implementations.</summary>
    public interface ISettingsStore
    {
        /// <summary>Returns persisted settings; never throws — degrades to defaults instead.</summary>
        AppSettings Load();

        void Save(AppSettings settings);
    }
}
