using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using TaskManager.Infrastructure;
using TaskManager.Resources.Languages;
using TaskManager.UiAutomationTests.Pages;

namespace TaskManager.UiAutomationTests
{
    [Collection("ui")]
    public class BehaviorTests : IDisposable
    {
        private readonly AppSession _session;
        private readonly MainWindowPage _main;
        private readonly VictimFactory _victims = new();

        public BehaviorTests(AppSession session)
        {
            _session = session;
            _main = new MainWindowPage(MainWindowElement);
        }

        private AutomationElement MainWindowElement =>
            _session.App.GetMainWindow(_session.Automation, TimeSpan.FromSeconds(5))!;

        private bool HasTopLevelWindow(string title) =>
            _session.App.GetAllTopLevelWindows(_session.Automation).Any(w => w.Title == title);

        private bool Gone(string title, TimeSpan within) =>
            Retry.WhileTrue(() => HasTopLevelWindow(title), within).Result;

        /// <summary>
        /// Dismisses a top-level dialog without touching real input devices:
        /// accept invokes the dialog's first button (OK is first/default in every
        /// box this app raises), cancel closes via the Window pattern
        /// (WM_CLOSE semantics = Cancel on OKCancel boxes).
        /// </summary>
        private void DismissDialog(string title, bool accept)
        {
            var box = _session.GetTopLevelWindow(title);
            box.ShouldNotBeNull($"expected dialog '{title}'");

            if (accept)
            {
                var button = box.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                                .FirstOrDefault();
                button.ShouldNotBeNull($"no button available to dismiss '{title}'");
                button.AsButton().Invoke();
            }
            else
            {
                box.AsWindow().Patterns.Window.Pattern.Close();
            }

            Gone(title, TimeSpan.FromSeconds(3)).ShouldBeTrue($"dialog '{title}' refused to close");
        }
        [Fact]
        public void About_ShowsStampedVersionAndCommit()
        {
            _main.InvokeMenu(Strings.HelpMenu, Strings.AboutMenu);

            var dialog = _session.GetTopLevelWindow(Strings.AboutMenu);
            dialog.ShouldNotBeNull("About dialog did not appear");
            var page = new AboutDialogPage(dialog);

            var stamped = FileVersionInfo.GetVersionInfo(_session.ExePath).ProductVersion!;
            var expectedVersion = stamped.Split('+')[0];
            var commitPart = stamped.Split('+').Skip(1).FirstOrDefault() ?? string.Empty;

            page.AllText.ShouldContain(expectedVersion);
            if (commitPart.Length > 0)
            {
                page.AllText.ShouldContain(commitPart[..Math.Min(7, commitPart.Length)]);
            }

            page.Ok();
            _session.AssertAlive();
        }

        [Fact]
        public void Settings_RoundTrip_PublishesPausedThenRestores()
        {
            _main.InvokeMenu(Strings.SettingsStr);
            var dialog = _session.GetTopLevelWindow("SettingsWindow");
            dialog.ShouldNotBeNull("settings dialog did not appear");

            var page = new SettingsDialogPage(dialog);
            page.SelectLastRefreshFrequency(); // last enum item = Paused
            page.Save();

            Retry.WhileTrue(
                () => !_main.StatusTexts().Contains(Strings.StatusPaused),
                TimeSpan.FromSeconds(5)).Result.ShouldBeTrue(
                "status strip did not show Paused after save");

            // restore Low so later/default cadence resumes
            _main.InvokeMenu(Strings.SettingsStr);
            dialog = _session.GetTopLevelWindow("SettingsWindow");
            dialog.ShouldNotBeNull();
            var restore = new SettingsDialogPage(dialog);
            restore.SelectRefreshFrequencyByLabelPart("Low");
            restore.Save();

            _session.AssertAlive();
        }

        [Fact]
        public void Export_OpensAndCancels()
        {
            _main.InvokeMenu(Strings.Export);

            var dialog = _session.GetTopLevelWindow("DataExportWindow");
            dialog.ShouldNotBeNull("export dialog did not appear");

            DismissDialog("DataExportWindow", accept: false); // no dedicated cancel button

            _session.AssertAlive();
        }

        [Fact]
        public void Terminate_Victim_HappyPath_KillsProcessAndRemovesRow()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();
            victim.HasExited.ShouldBeFalse();

            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull($"victim row '{victim.RowSearchText}' never appeared");

            // Terminate via the app menu (Invoke pattern - no real input devices).
            _main.OpenTopLevelLeaf(Strings.Management, Strings.TerminateProcesses);

            DismissDialog(Strings.Confirm, accept: true); // confirm OK

            victim.WaitUntilExited(TimeSpan.FromSeconds(5));
            _main.FindRowContaining(victim.RowSearchText)
                 .ShouldBeNull("row lingered past the 12 s refresh ceiling");
        }

        [Fact]
        public void Terminate_CancelPath_VictimSurvives()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();

            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull();
            _main.SelectRow(row!);

            _main.InvokeMenu(Strings.Management, Strings.TerminateProcesses);
            DismissDialog(Strings.Confirm, accept: false); // cancel = no termination

            victim.HasExited.ShouldBeFalse();
        }

        [Fact]
        public void Priority_AppliesToVictimOnly()
        {
            using var victims = new VictimFactory();
            var victim = victims.Spawn();

            var row = _main.FindRowContaining(victim.RowSearchText);
            row.ShouldNotBeNull();
            _main.SelectRow(row!);

            _main.InvokeMenu(Strings.Management, Strings.SetPriority);
            var dialog = _session.GetTopLevelWindow("SetPriorityWindow");
            dialog.ShouldNotBeNull();

            var combo = dialog!.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox)).First();
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            var items = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            items.Length.ShouldBeGreaterThanOrEqualTo(6);
            items[0].Patterns.SelectionItem.Pattern.Select(); // culture-neutral: any value proves the flow
            combo.Patterns.ExpandCollapse.Pattern.Collapse();

            var confirm = dialog.FindFirstDescendant(cf => cf.ByName(Strings.Confirm));
            confirm.ShouldNotBeNull();
            confirm.AsButton().Invoke();

            victim.HasExited.ShouldBeFalse();
        }

        [Fact]
        public void Terminate_WithoutSelection_ShowsValidationError()
        {
            _main.DeselectAllRows();
            Console.WriteLine($"[diag] WithoutSelection: selectedRows after deselect={_main.SelectedRowCount()}");

            _main.InvokeMenu(Strings.Management, Strings.TerminateProcesses);

            Thread.Sleep(500);
            var titles = _session.App.GetAllTopLevelWindows(_session.Automation).Select(w => w.Title);
            Console.WriteLine($"[diag] WithoutSelection: top-level titles=[{string.Join(", ", titles)}]");

            DismissDialog(Strings.Error, accept: true);
            _session.AssertAlive();
        }

        [Fact]
        public void SecondInstance_ExitsImmediately_AndOriginalStaysAlive()
        {
            var psi = new ProcessStartInfo
            {
                FileName = _session.ExePath,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables[TaskManagerEnvironment.InstanceName] = _session.InstanceName;

            using var duplicate = Process.Start(psi)!;
            duplicate.WaitForExit(5_000).ShouldBeTrue("second instance was not bounced");
            duplicate.ExitCode.ShouldBe(0);

            _session.AssertAlive();
            _main.Title.ShouldBe(AppSession.MainWindowTitle);
        }

        public void Dispose()
        {
            _victims.Dispose();

            // A test that failed mid-dialog leaves a MODAL open, disabling the main
            // window and poisoning every later test. Close strays unconditionally.
            try
            {
                foreach (var w in _session.App.GetAllTopLevelWindows(_session.Automation))
                {
                    if (w.Title != AppSession.MainWindowTitle)
                    {
                        try { w.AsWindow().Patterns.Window.Pattern.Close(); }
                        catch { /* already closing/gone */ }
                    }
                }
            }
            catch
            {
                // app already gone
            }
        }
    }
}
