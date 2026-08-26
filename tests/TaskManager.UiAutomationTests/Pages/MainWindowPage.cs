using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace TaskManager.UiAutomationTests.Pages
{
    /// <summary>
    /// Behavioral lookups on the main window. Names come from visible content or
    /// the app's own localized resources - never from internal layout.
    /// </summary>
    public sealed class MainWindowPage
    {
        private readonly Window _window;

        public MainWindowPage(AutomationElement window) => _window = window.AsWindow();

        public string Title => _window.Title ?? string.Empty;

        public IReadOnlyList<string> StatusTexts()
        {
            var result = Retry.WhileEmpty(
                () => _window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                             .Select(e => e.Name)
                             .Where(n => !string.IsNullOrEmpty(n))
                             .ToArray(),
                TimeSpan.FromSeconds(5));
            return result.Result ?? [];
        }

        public AutomationElement? FindRowContaining(string text)
        {
            var result = Retry.WhileNull(
                () => _window.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                             .FirstOrDefault(r => r.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true),
                TimeSpan.FromSeconds(12),
                TimeSpan.FromMilliseconds(250));
            return result.Result;
        }

        public void SelectRow(AutomationElement row) =>
            row.Patterns.SelectionItem.Pattern.Select();

        public void DeselectAllRows()
        {
            foreach (var row in _window.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
            {
                var selection = row.Patterns.SelectionItem;
                if (selection.IsSupported && selection.Pattern.IsSelected.Value)
                {
                    selection.Pattern.RemoveFromSelection();
                }
            }
        }

        /// <summary>
        /// Walks menu headers parent-to-leaf, invoking the leaf. Expands parents
        /// defensively before searching children.
        /// </summary>
        public void InvokeMenu(params string[] headers)
        {
            AutomationElement current = _window;
            foreach (var header in headers)
            {
                var element = Retry.WhileNull(
                    () => current.FindFirstDescendant(cf => cf.ByName(header)),
                    TimeSpan.FromSeconds(5)).Result;

                element.ShouldNotBeNull($"menu element '{header}' not found");

                var expandCollapse = element.Patterns.ExpandCollapse;
                if (expandCollapse.IsSupported && ReferenceEquals(current, _window))
                {
                    expandCollapse.Pattern.Expand();
                }

                current = element;
            }

            var invoke = current.Patterns.Invoke;
            if (invoke.IsSupported)
            {
                invoke.Pattern.Invoke();
            }
        }
    }
}
