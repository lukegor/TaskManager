using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;

namespace TaskManager.Services
{
    /// <summary>
    /// Single mutation pipeline for user settings: validate → persist → swap → notify.
    /// Persistence failures surface as exceptions to the caller's error guard; the
    /// in-memory snapshot is only swapped after a successful save.
    /// </summary>
    internal sealed class SettingsService : ISettingsService
    {
        private readonly ISettingsStore _store;
        private readonly ILogger<SettingsService> _logger;

        public AppSettings Current { get; private set; }

        public event Action<AppSettings>? Changed;

        public SettingsService(ISettingsStore store, ILogger<SettingsService> logger)
        {
            _store = store;
            _logger = logger;

            Current = store.Load();

            if (!AppSettings.TryValidate(Current, out var error))
            {
                // Store already degrades per-field; this guards against future regressions.
                _logger.LogWarning("Loaded settings failed validation ({Error}); using defaults", error);
                Current = AppSettings.Defaults;
            }
        }

        public void Update(AppSettings settings)
        {
            if (!AppSettings.TryValidate(settings, out var error))
            {
                throw new SettingsValidationException(error);
            }

            _store.Save(settings);

            var previous = Current;
            Current = settings;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Settings updated (language: {Before} -> {After})",
                    previous.Language, settings.Language);
            }

            Changed?.Invoke(settings);
        }
    }
}
