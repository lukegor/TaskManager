using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Represents Windows System Process data (core business logic).
    /// Raises per-field change notifications so bound grid rows update in place.
    /// </summary>
    public partial class Process : ObservableObject, IExportable
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private int? _pid;

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private int? _priority;

        [ObservableProperty]
        private int _threadCount;

        [ObservableProperty]
        private int? _ppid;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ArchitectureTypeDisplay))]
        private ArchitectureType _architectureType;

        [IgnoreSerialization]
        [JsonIgnore]
        public string ArchitectureTypeDisplay => EnumExtensions.ToString(ArchitectureType);

        public override string ToString() => $"{Name} ({Pid})";

        public string ToDelimitedString(char separator)
        {
            return string.Join(separator.ToString(),
                Name,
                Pid,
                Path,
                Priority,
                ThreadCount,
                Ppid);
        }
    }
}
