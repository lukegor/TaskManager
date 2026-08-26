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

        /// <summary>
        /// Finds a row whose name contains the text. DataGrid rows are virtualized
        /// (only viewport-realized rows surface to UIA) and the process list is
        /// unsorted, so this scans the visible page and pages through the entire
        /// grid within the ceiling instead of trusting one subtree query.
        /// </summary>
        public AutomationElement? FindRowContaining(string text)
        {
            ResetGridScroll();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            var pages = 0;
            while (DateTime.UtcNow < deadline)
            {
                var row = ScanViewportFor(text);
                if (row is not null)
                {
                    Console.WriteLine($"[diag] row '{text}' found after {pages} page-downs");
                    return row;
                }

                if (!TryPageDown())
                {
                    ResetGridScroll(); // swept bottom without a hit - sweep again until ceiling
                }
                else
                {
                    pages++;
                }

                Thread.Sleep(200);
            }

            var bar = VerticalScrollBar();
            var rangeSupported = bar?.Patterns.RangeValue?.IsSupported == true;
            Console.WriteLine(
                $"[diag] row '{text}' NOT FOUND: realizedRows={RealizedRows().Length} " +
                $"sample=[{string.Join(" | ", RealizedRows().Take(3).Select(r => r.Name))}] " +
                $"scrollbar={bar != null} rangeSupported={rangeSupported} " +
                (bar != null && rangeSupported
                    ? $"min={bar.Patterns.RangeValue.Pattern.Minimum} val={bar.Patterns.RangeValue.Pattern.Value} max={bar.Patterns.RangeValue.Pattern.Maximum} large={bar.Patterns.RangeValue.Pattern.LargeChange} "
                    : string.Empty) +
                $"pageDowns={pages}");
            return null;        }

        public void SelectRow(AutomationElement row) =>
            row.Patterns.SelectionItem.Pattern.Select();

        public void DeselectAllRows()
        {
            ResetGridScroll();
            do
            {
                foreach (var row in RealizedRows())
                {
                    TryDeselect(row);
                }
            }
            while (TryPageDown());
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

                if (element is null)
                {
                    var childNames = _window.FindAllChildren()
                                            .Select(c => $"{c.ControlType}: {c.Name}")
                                            .ToArray();
                    Console.WriteLine(
                        $"[diag] menu '{header}' missing; window children: [{string.Join(", ", childNames)}]");
                }

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

        public void OpenTopLevelLeaf(string topLevelHeader, string leafHeader) =>
            InvokeMenu(topLevelHeader, leafHeader);

        private AutomationElement? ScanViewportFor(string text)
        {
            try
            {
                return RealizedRows()
                    .FirstOrDefault(r => r.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch
            {
                return null; // element tree invalidated mid-refresh tick - retried by the sweep
            }
        }

        private AutomationElement[] RealizedRows()
        {
            try
            {
                return _window.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
            }
            catch
            {
                return [];
            }
        }

        private void TryDeselect(AutomationElement row)
        {
            try
            {
                var selection = row.Patterns.SelectionItem;
                if (selection.IsSupported && selection.Pattern.IsSelected.Value)
                {
                    selection.Pattern.RemoveFromSelection();
                }
            }
            catch
            {
                // stale mid-refresh row - a later page pass catches stragglers
            }
        }

        public int SelectedRowCount()
        {
            var count = 0;
            foreach (var row in RealizedRows())
            {
                TryCount(row, ref count);
            }
            return count;
        }

        private void TryCount(AutomationElement row, ref int count)
        {
            try
            {
                var selection = row.Patterns.SelectionItem;
                if (selection.IsSupported && selection.Pattern.IsSelected.Value)
                {
                    count++;
                }
            }
            catch
            {
                // stale row - skip
            }
        }

        /// <summary>The grid's vertical scrollbar. Orientation support is unreliable across
        /// WPF peers, so candidate bars are also disambiguated geometrically.</summary>
        private AutomationElement? VerticalScrollBar()
        {
            AutomationElement[] bars;
            try
            {
                bars = _window.FindAllDescendants(cf => cf.ByControlType(ControlType.ScrollBar));
            }
            catch
            {
                return null;
            }

            if (bars.Length > 0)
            {
                Console.WriteLine($"[diag] scrollbar candidates={bars.Length}");
            }

            foreach (var bar in bars)
            {
                try
                {
                    var orientation = bar.Properties.Orientation;
                    if (orientation.IsSupported && orientation.Value == OrientationType.Vertical)
                    {
                        return bar;
                    }
                }
                catch
                {
                    // orientation unsupported on this peer - judge by shape instead
                }

                try
                {
                    var rect = bar.BoundingRectangle;
                    if (rect.Height > rect.Width)
                    {
                        return bar;
                    }
                }
                catch
                {
                    // unreachable element - try the next candidate
                }
            }

            return null;
        }

        private bool DiagEnabled => Environment.GetEnvironmentVariable("TMUITEST_DIAG") == "1";

        /// <summary>Advances one viewport down via the scrollbar's range value. False at bottom.</summary>
        private bool TryPageDown()
        {
            var bar = VerticalScrollBar();
            var range = bar?.Patterns.RangeValue;
            if (bar is not null && range is not null && range.IsSupported)
            {
                try
                {
                    var pattern = range.Pattern;
                    var step = pattern.LargeChange > 0 ? pattern.LargeChange : Math.Max(pattern.SmallChange, 1);
                    var target = Math.Min(pattern.Value + step, pattern.Maximum);
                    if (target > pattern.Value)
                    {
                        pattern.SetValue(target);
                        Thread.Sleep(100); // let WPF realize the newly scrolled-in rows
                        return true;
                    }
                }
                catch
                {
                    // range manipulation refused - paging stops this pass; the sweep retries
                }
            }

            return false;
        }

        private string? FirstRealizedRowName()
        {
            try
            {
                return RealizedRows().FirstOrDefault()?.Name;
            }
            catch
            {
                return null;
            }
        }

        private void ResetGridScroll()
        {
            var bar = VerticalScrollBar();
            var range = bar?.Patterns.RangeValue;
            if (bar is null || range is null || !range.IsSupported)
            {
                return;
            }

            try
            {
                if (range.Pattern.Value > range.Pattern.Minimum)
                {
                    range.Pattern.SetValue(range.Pattern.Minimum);
                }
            }
            catch
            {
                // best-effort positioning
            }
        }
    }
}
