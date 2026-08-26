using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using TaskManager.Resources.Languages;

namespace TaskManager.UiAutomationTests.Pages
{
    /// <summary>Automates the settings dialog: second ComboBox is refresh-frequency.</summary>
    public sealed class SettingsDialogPage
    {
        private readonly AutomationElement _dialog;

        public SettingsDialogPage(AutomationElement dialog) => _dialog = dialog;

        /// <summary>Selects the last item of the frequency combo (enum order ends with Paused).</summary>
        public void SelectLastRefreshFrequency()
        {
            var combos = _dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox));
            combos.Length.ShouldBeGreaterThanOrEqualTo(2);

            var frequency = combos[1];
            frequency.Patterns.ExpandCollapse.Pattern.Expand();

            var items = frequency.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            items.Length.ShouldBeGreaterThanOrEqualTo(4);
            items[^1].Patterns.SelectionItem.Pattern.Select();
            frequency.Patterns.ExpandCollapse.Pattern.Collapse();
        }

        /// <summary>Restores an explicit frequency by matching the visible item label.</summary>
        public void SelectRefreshFrequencyByLabelPart(string labelPart)
        {
            var combos = _dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox));
            var frequency = combos[1];
            frequency.Patterns.ExpandCollapse.Pattern.Expand();

            var items = frequency.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            var match = items.FirstOrDefault(i => i.Name?.Contains(labelPart, StringComparison.OrdinalIgnoreCase) == true);
            match.ShouldNotBeNull($"no frequency item matching '{labelPart}'");
            match.Patterns.SelectionItem.Pattern.Select();
            frequency.Patterns.ExpandCollapse.Pattern.Collapse();
        }

        public void Save()
        {
            var save = _dialog.FindFirstDescendant(cf => cf.ByName(Strings.Save));
            save.ShouldNotBeNull("Save button not found");
            save.AsButton().Invoke();
        }
    }
}
