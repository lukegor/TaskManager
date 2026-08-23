using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Domain.Services.Utility;
using TaskManager.Infrastructure.Logging;
using TaskManager.Properties;
using TaskManager.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.Services.Factories;
using TaskManager.UI.Views;
using TaskManager.Utility.Utility;
using TaskManager.ViewModels;

namespace TaskManager
{
    public delegate void App_Close(object sender);

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider = null!;

        public static event App_Close? App_Close;

        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            _serviceProvider = serviceCollection.BuildServiceProvider();

            SetLanguage();

            base.OnStartup(e);

            LaunchGUI();
        }

        private void SetLanguage()
        {
            LanguageDictionary Languages = new LanguageDictionary();

            CultureInfo culture;
            //#if DEBUG
            //            culture = CultureInfo.InvariantCulture; // Force invariant culture for debugging
            //#else
            if (!string.IsNullOrEmpty(Settings.Default.LanguageVersion) && Languages.ContainsKey(Settings.Default.LanguageVersion))
            {
                culture = Languages[key: Settings.Default.LanguageVersion];
            }
            else culture = CultureInfo.CurrentCulture; // Use the current culture in release mode
                                                       //#endif
                                                       // Set default culture for the application - seems unnecessary
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
            services.AddSingleton<IAppSettings, SettingsService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IErrorHandler, UiErrorHandler>();
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();

            services.AddSingleton<SettingsService>();
            services.AddSingleton<ProcessManager>();
            services.AddSingleton<TimerManager>();
            services.AddTransient<FolderSelector>();

            services.AddSingleton<DataExporterFactory>(); // services.AddTransient<DataExporterFactory>();
            services.AddTransient<DataExportViewModelFactory>();
            services.AddTransient<SetPriorityVVmFactory>();

            // Register ViewModels
            services.AddSingleton<MainWindowViewModel>();
            //services.AddTransient<SetPriorityWindowViewModel>();
            //services.AddTransient<DataExportWindowViewModel>();
            //services.AddTransient<SettingsWindowViewModel>();

            // Register Views
            services.AddSingleton<MainWindow>(sp =>
            {
                return new MainWindow
                {
                    DataContext = sp.GetRequiredService<MainWindowViewModel>()
                };
            });

            //services.AddTransient<SetPriorityWindow>();
            services.AddTransient<DataExportWindow>();
            //services.AddTransient<SettingsWindow>();
        }

        private void LaunchGUI() {
            MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e) {
            App_Close?.Invoke(this);
        }

        internal static void Restart()
        {
            var currentExecutablePath = Environment.ProcessPath;
            System.Diagnostics.Process.Start(currentExecutablePath!);
            Application.Current.Shutdown();
        }
    }
}
