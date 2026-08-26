using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using TaskManager.Resources.Languages;

namespace TaskManager.UiAutomationTests.Pages
{
    public sealed class AboutDialogPage
    {
        private readonly AutomationElement _dialog;

        public AboutDialogPage(AutomationElement dialog) => _dialog = dialog;

        /// <summary>Concatenated visible text of every Text element in the dialog.</summary>
        public string AllText =>
            string.Join("\n", _dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                                     .Select(t => t.Name));

        public void Ok()
        {
            var ok = _dialog.FindFirstDescendant(cf => cf.ByName(Strings.AboutOkButton));
            ok.ShouldNotBeNull("OK button not found");
            ok.AsButton().Invoke();
        }
    }
}
