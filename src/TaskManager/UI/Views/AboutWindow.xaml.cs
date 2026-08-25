using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using TaskManager.Services;

namespace TaskManager.UI.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionRun.Text = AboutInfo.Version;
            CommitRun.Text = string.IsNullOrEmpty(AboutInfo.Commit)
                ? "-"
                : AboutInfo.Commit;
        }

        private void OnOpenRepository(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
