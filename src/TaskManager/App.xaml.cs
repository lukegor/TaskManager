using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Primitives;
using TaskManager.Infrastructure;
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
        private static SingleInstanceGuard? _guard;

        protected override void OnStartup(StartupEventArgs e)
        {
            var awaitInstance = e.Args.Contains("--await-instance", StringComparer.Ordinal);
            _guard = new SingleInstanceGuard(awaitInstance);
            if (!_guard.IsFirstInstance)
            {
                _guard.ActivateFirstInstanceWindow();
                Shutdown();
                return;
            }

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

        /// <summary>
        /// Logs intent while the log pipeline is still alive, then disposes the
        /// container so every registered IDisposable (logger providers included)
        /// can flush/release. Must stay last-write-wins over any shutdown work.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            // Second-instance startups exit before the container is built.
            if (_serviceProvider is not null)
            {
                _serviceProvider.GetRequiredService<ILogger<App>>()
                    .LogInformation("Application exiting with code {ExitCode}", e.ApplicationExitCode);

                (_serviceProvider as IDisposable)?.Dispose();
            }

            _guard?.Dispose();

            base.OnExit(e);
        }

        private void SetLanguage()
        {
            var settings = _serviceProvider.GetRequiredService<ISettingsService>();
            var savedLanguage = settings.Current.Language;
            LanguageDictionary Languages = new LanguageDictionary();

            CultureInfo culture;
            if (!string.IsNullOrEmpty(savedLanguage) && Languages.TryGetValue(savedLanguage, out var savedCulture))
            {
                culture = savedCulture;
            }
            else culture = CultureInfo.CurrentCulture; // Use the current culture in release mode
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Set current culture for the current thread - NECESSARY
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(ResolveMinimumLogLevel());
                logging.AddProvider(new FileLoggerProvider());
            });

            services.AddCoreServices();
            services.AddUiServices();
        }

        /// <summary>
        /// TASKMANAGER_LOGLEVEL overrides; otherwise Debug under a debugger, else Information.
        /// </summary>
        internal static LogLevel ResolveMinimumLogLevel() =>
            TryResolveMinimumLogLevel(Environment.GetEnvironmentVariable("TASKMANAGER_LOGLEVEL"), out var parsed)
                ? parsed
                : Debugger.IsAttached ? LogLevel.Debug : LogLevel.Information;

        internal static bool TryResolveMinimumLogLevel(string? raw, out LogLevel level)
        {
            return Enum.TryParse(raw, ignoreCase: true, out level)
                   && Enum.IsDefined(level);
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
            using var successor = System.Diagnostics.Process.Start(currentExecutablePath!, "--await-instance");
            _guard?.Dispose(); // successor waits for release (handoff mode)
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Relaunches the app requesting elevation. A declined UAC prompt is a silent
        /// no-op (mutex untouched); a successful spawn hands the mutex off and exits.
        /// Any other failure propagates to the caller's error handling.
        /// </summary>
        internal static void RelaunchElevated()
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--await-instance",
            };

            try
            {
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return; // user declined elevation: stay running, still protected
            }

            _guard?.Dispose();
            Application.Current.Shutdown();
        }
    }
}
