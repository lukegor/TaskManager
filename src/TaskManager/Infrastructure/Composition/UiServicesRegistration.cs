using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Views;
using TaskManager.ViewModels;

namespace TaskManager.Infrastructure.Composition
{
    /// <summary>Registrations for dialogs, reporting, and view/viewmodel wiring.</summary>
    internal static class UiServicesRegistration
    {
        public static IServiceCollection AddUiServices(this IServiceCollection services)
        {
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IErrorHandler, UiErrorHandler>();
            services.AddSingleton<IFolderPicker, FolderPicker>();

            services.AddSingleton<Func<DataType, BaseDataExporter>>(sp => dataType => dataType switch
            {
                DataType.Csv => new CsvExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<CsvExporter>>()),
                DataType.Txt => new TxtExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<TxtExporter>>()),
                DataType.Xlsx => new ExcelExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<ExcelExporter>>()),
                DataType.Json => new JsonExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<JsonExporter>>()),
                DataType.Xml => new XmlExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<XmlExporter>>()),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            });

            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>(sp => new MainWindow
            {
                DataContext = sp.GetRequiredService<MainWindowViewModel>()
            });
            return services;
        }
    }
}
