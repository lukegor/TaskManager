using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Abstractions
{
    public interface IAppSettings
    {
        string Language { get; }
        RefreshFrequencyType RefreshFrequency { get; }
        string DateTimeFormat { get; }
    }
}
