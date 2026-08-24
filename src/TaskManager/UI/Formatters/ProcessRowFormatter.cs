using TaskManager.Domain.Models;

namespace TaskManager.UI.Formatters
{
    /// <summary>
    /// Formats a <see cref="ProcessItem"/> as its tab-delimited process text,
    /// byte-identical to the pre-refactor copy output. Anything else falls back to ToString().
    /// </summary>
    public class ProcessRowFormatter : IRowTextFormatter
    {
        public string Format(object? item)
        {
            return item is ProcessItem processItem
                ? processItem.Process.ToDelimitedString('\t')
                : item?.ToString() ?? string.Empty;
        }
    }
}
