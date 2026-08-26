using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Resources = TaskManager.Resources.Languages.Strings;

namespace TaskManager.UiAutomationTests.Pages
{
    /// <summary>
    /// Behavioral lookups on the main window. Names come from visible content or
    /// the app's own localized resources - never from internal layout.
    /// </summary>
    public sealed class MainWindowPage
    {
        private readonly Application _app;
        private readonly UIA3Automation _automation;
        private readonly Window _window;

        public MainWindowPage(Application app, UIA3Automation automation, AutomationElement window)
        {
            _app = app;
            _automation = automation;
            _window = window.AsWindow();
        }

        public string Title => _window.Title ?? string.Empty;

        /// <summary>Finds a row whose name contains the text. The grid runs without row
        /// virtualization under the test hook (TASKMANAGER_UITEST=1), so a single
        /// bounded query suffices; the retry ceiling absorbs refresh ticks.</summary>
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

        public int SelectedRowCount()
        {
            var count = 0;
            foreach (var row in _window.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
            {
                var selection = row.Patterns.SelectionItem;
                if (selection.IsSupported && selection.Pattern.IsSelected.Value)
                {
                    count++;
                }
            }

            return count;
        }

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

        /// <summary>
        /// Walks menu headers parent-to-leaf, invoking the leaf. Expands parents so
        /// submenus surface; expanded WPF menus may reparent items into popup
        /// windows, so misses fall back to sweeping every top-level window of the
        /// process before failing.
        /// </summary>
        public void InvokeMenu(params string[] headers)
        {
            AutomationElement current = _window;
            foreach (var header in headers)
            {
                // Expanded WPF menus reparent submenus into popup windows: prefer the
                // LIVE popup instance (sweep across top-level windows) over the stale
                // pre-expansion container that may linger in the parent's logical
                // subtree - invoking the stale peer silently no-ops the command.
                var element = FindMenuElementAnywhere(header) ??
                              FindMenuElement(parent, header);

                if (element is null)
                {
                    var childNames = current.FindAllChildren()
                                            .Select(c => $"{c.ControlType}: {c.Name}")
                                            .ToArray();
                    Console.WriteLine(
                        $"[diag] menu '{header}' missing; parent children: [{string.Join(", ", childNames)}]");
                }

                element.ShouldNotBeNull($"menu element '{header}' not found");

                current = element;

                var expandCollapse = current.Patterns.ExpandCollapse;
                if (expandCollapse.IsSupported && expandCollapse.Pattern.ExpandCollapseState != ExpandCollapseState.Expanded)
                {
                    expandCollapse.Pattern.Expand();
                }
            }

            var invoke = current.Patterns.Invoke;
            invoke.IsSupported.ShouldBeTrue($"leaf '{current.Name}' does not support Invoke");
            invoke.Pattern.Invoke();
        }

        public void OpenTopLevelLeaf(string topLevelHeader, string leafHeader) =>
            InvokeMenu(topLevelHeader, leafHeader);

        private AutomationElement? FindMenuElement(AutomationElement parent, string header)
        {
            var result = Retry.WhileNull(
                () => parent.FindFirstDescendant(cf => cf.ByName(header)),
                TimeSpan.FromSeconds(3));
            return result.Result;
        }

        private AutomationElement? FindMenuElementAnywhere(string header)
        {
            var result = Retry.WhileNull(
                () => _app.GetAllTopLevelWindows(_automation)
                        .SelectMany(w => w.FindAllDescendants(cf => cf.ByName(header)))
                        .FirstOrDefault(),
                TimeSpan.FromSeconds(3));
            return result.Result;
        }
    }
}
