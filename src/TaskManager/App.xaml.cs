using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;
using TaskManager.Infrastructure.Composition;
using TaskManager.Infrastructure.Logging;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Views;
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

            services.AddCoreServices();
            services.AddUiServices();
        }

        private void LaunchGUI()
        {
            MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Single startup point: initial load + polling start. Failures route through
            // IErrorHandler.GuardAsync inside InitializeCommand and are logged, not fatal.
            _ = _serviceProvider.GetRequiredService<MainWindowViewModel>().InitializeCommand.ExecuteAsync(null);
        }

        internal static void Restart()
        {
            var currentExecutablePath = Environment.ProcessPath;
            System.Diagnostics.Process.Start(currentExecutablePath!);
            Application.Current.Shutdown();
        }
    }
}
