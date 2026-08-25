using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Infrastructure.Settings;
using TaskManager.Presentation;
using TaskManager.Services;

namespace TaskManager.Infrastructure.Composition
{
    /// <summary>Registrations for core/presentation state and OS-facing services.</summary>
    internal static class CoreServicesRegistration
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, string? settingsDirectory)
        {
            services.AddSingleton<ISettingsStore>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<JsonSettingsStore>>();
                return string.IsNullOrEmpty(settingsDirectory)
                    ? new JsonSettingsStore(logger)
                    : new JsonSettingsStore(settingsDirectory, logger);
            });
            services.AddSingleton<SettingsService>();
            services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<ISystemProcessEnumerator, NtSystemProcessEnumerator>();
            services.AddSingleton<ProcessEnricher>();
            services.AddSingleton<ProcessOperationsService>();
            services.AddSingleton<IProcessOperations>(sp => sp.GetRequiredService<ProcessOperationsService>());
            services.AddSingleton<ProcessListCatalog>();
            services.AddSingleton<IProcessListCatalog>(sp => sp.GetRequiredService<ProcessListCatalog>());
            services.AddSingleton(TimeProvider.System);
            return services;
        }
    }
}
