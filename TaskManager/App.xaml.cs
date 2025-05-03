using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Security.Cryptography;
using Task_Manager.UI.Views;
using Task_Manager.Services;
using Task_Manager.Utility;
using System.Runtime.InteropServices;
using System.Globalization;
using Task_Manager.Properties;

namespace Task_Manager
{
    public delegate void App_Close(object sender);

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static event App_Close App_Close;

        protected override void OnStartup(StartupEventArgs e)
        {
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

        private void LaunchGUI() {
            MainWindow mainWindow = new MainWindow();
            MainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e) {
            App_Close?.Invoke(this);
        }

        internal void Restart()
        {
            var currentExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(currentExecutablePath);
            Application.Current.Shutdown();
        }
    }
}
