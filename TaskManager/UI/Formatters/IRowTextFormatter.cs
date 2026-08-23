namespace TaskManager.UI.Formatters
{
    /// <summary>
    /// Converts a grid row's bound data object into clipboard text.
    /// Keeps the generic grid free of domain knowledge.
    /// </summary>
    public interface IRowTextFormatter
    {
        string Format(object? item);
    }
}
