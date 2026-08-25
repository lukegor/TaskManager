using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Services;
using TaskManager.Infrastructure.Logging;
using TaskManager.Infrastructure.Settings;
using TaskManager.Presentation;
using TaskManager.Services.ErrorHandling;
using TaskManager.Domain.Services.DataExport;
using TaskManager.UI.Views;
using TaskManager.Domain.Primitives;
using TaskManager.ViewModels;

namespace TaskManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            _serviceProvider = serviceCollection.BuildServiceProvider();

            RegisterGlobalExceptionHandlers();

            SetLanguage();

            base.OnStartup(e);

            LaunchGUI();
        }

        private void RegisterGlobalExceptionHandlers()
        {
            var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();

            DispatcherUnhandledException += (_, e) =>
                e.Handled = errorHandler.HandleDispatcherException(e.Exception);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    errorHandler.LogFatal(exception);
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                errorHandler.LogUnobserved(e.Exception);
                e.SetObserved();
            };
        }

        private void SetLanguage()
        {
            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            var savedLanguage = settings.Current.Language;
            LanguageDictionary Languages = new LanguageDictionary();

            CultureInfo culture;
            if (!string.IsNullOrEmpty(savedLanguage) && Languages.ContainsKey(savedLanguage))
            {
                culture = Languages[key: savedLanguage];
            }
            else culture = CultureInfo.CurrentCulture; // Use the current culture in release mode
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Set current culture for the current thread - NECESSARY
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new FileLoggerProvider());
            });

            // Register Services
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<SettingsService>();
            services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IErrorHandler, UiErrorHandler>();
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<ISystemProcessEnumerator, NtSystemProcessEnumerator>();
            services.AddSingleton<ProcessEnricher>();

            services.AddSingleton<ProcessOperationsService>();
            services.AddSingleton<IProcessOperations>(sp => sp.GetRequiredService<ProcessOperationsService>());
            services.AddSingleton<ProcessListCatalog>();
            services.AddSingleton<IProcessListCatalog>(sp => sp.GetRequiredService<ProcessListCatalog>());
            services.AddSingleton<IFolderPicker, FolderPicker>();
            services.AddSingleton<Func<DataType, BaseDataExporter>>(sp => dataType => dataType switch
            {
                DataType.Csv => new CsvExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Txt => new TxtExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Xlsx => new ExcelExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Json => new JsonExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                DataType.Xml => new XmlExporter(sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ILogger<BaseDataExporter>>()),
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
            });
            services.AddSingleton<IWindowService, WindowService>();

            // Register ViewModels
            services.AddSingleton<MainWindowViewModel>();

            // Register Views
            services.AddSingleton<MainWindow>(sp =>
            {
                return new MainWindow
                {
                    DataContext = sp.GetRequiredService<MainWindowViewModel>()
                };
            });
        }

        private void LaunchGUI()
        {
            MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Single startup point: initial load + polling start. Failures route through
            // IErrorHandler.GuardAsync inside InitializeCommand and are logged, not fatal.
            _ = _serviceProvider.GetRequiredService<MainWindowViewModel>().InitializeCommand.ExecuteAsync(null);
        }

        private void Application_Exit(object sender, ExitEventArgs e) {
        }

        internal static void Restart()
        {
            var currentExecutablePath = Environment.ProcessPath;
            System.Diagnostics.Process.Start(currentExecutablePath!);
            Application.Current.Shutdown();
        }
    }
}
