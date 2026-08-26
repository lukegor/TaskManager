using System.Globalization;
using System.Text.RegularExpressions;
using TaskManager.Resources.Languages;
using TaskManager.UiAutomationTests.Pages;

namespace TaskManager.UiAutomationTests
{
    [Collection("ui")]
    public class SmokeLaunchTests
    {
        private readonly AppSession _session;

        public SmokeLaunchTests(AppSession session) => _session = session;

        [Fact]
        public void Launch_ShowsLiveMainWindow()
        {
            _session.AssertAlive();

            var window = _session.App.GetMainWindow(_session.Automation, TimeSpan.FromSeconds(5));
            var page = new MainWindowPage(window);

            page.Title.ShouldBe(AppSession.MainWindowTitle);

            var statusTexts = page.StatusTexts();
            statusTexts.ShouldNotBeEmpty();

            var countPattern = $"^{Regex.Escape(Strings.Processes)}: [1-9][0-9]*$";
            statusTexts.ShouldContain(t => Regex.IsMatch(t, countPattern),
                $"expected a '{Strings.Processes}: N' counter among [{string.Join(", ", statusTexts)}]");

            statusTexts.ShouldContain(string.Format(CultureInfo.CurrentCulture,
                Strings.StatusEverySeconds, 10)); // Low frequency default maps to 10 s
        }
    }
}
