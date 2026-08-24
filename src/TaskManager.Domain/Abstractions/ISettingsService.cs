using TaskManager.Domain.Models;

namespace TaskManager.Domain.Abstractions
{
    /// <summary>
    /// Single access point for user settings: an immutable <see cref="AppSettings"/> snapshot,
    /// an explicit change event, and one update pipeline (validate → persist → swap → notify).
    /// </summary>
    /// <remarks>
    /// Threading contract: <see cref="Changed"/> fires synchronously on the thread that called
    /// <see cref="Update"/>. Handlers that touch UI-bound state must marshal themselves.
    /// </remarks>
    public interface ISettingsService
    {
        AppSettings Current { get; }

        event Action<AppSettings>? Changed;

        void Update(AppSettings settings);
    }
}
