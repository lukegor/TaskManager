using System.Windows;
using System.Windows.Media;
using System.Security.Cryptography;
using TaskManager.UI.Views;
using TaskManager.Services;
using System.Runtime.InteropServices;
using System.Globalization;
using TaskManager.Properties;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.ViewModels;
using TaskManager.Services.Data_Export;
using TaskManager.Domain.Services;
using TaskManager.Domain.Services.Utility;
using TaskManager.Utility.Utility;
using TaskManager.Services.Factories;
using TaskManager.Domain.Abstractions;

namespace TaskManager
{
    public delegate void App_Close(object sender);

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider;

        public static event App_Close App_Close;

        protected override void OnStartup(StartupEventArgs e)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            _serviceProvider = serviceCollection.BuildServiceProvider();

            SetLanguage();

            base.OnStartup(e);

            var SettingsProcessor = new SettingsProcessor();
            SettingsProcessor.ProcessPropertyValues();

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
            services.AddSingleton<IAppSettings, WpfAppSettings>();

            // Register Services
            services.AddSingleton<SettingsProcessor>();
            services.AddSingleton<ProcessManager>();
            services.AddSingleton<TimerManager>();
            services.AddTransient<FolderSelector>();

            services.AddSingleton<IDispatcherService, WpfDispatcherService>();

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

        internal void Restart()
        {
            var currentExecutablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            System.Diagnostics.Process.Start(currentExecutablePath);
            Application.Current.Shutdown();
        }
    }
}
