using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

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
        private int _pid;

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

        /// <summary>Single field-mapping point from a fresh snapshot plus enrichment.</summary>
        public static Process FromSnapshot(ProcessSnapshot snapshot, ProcessEnrichment enrichment) => new()
        {
            Name = snapshot.Name,
            Pid = snapshot.Pid,
            Path = enrichment.Path,
            ArchitectureType = enrichment.Architecture,
            Priority = snapshot.BasePriority,
            ThreadCount = snapshot.ThreadCount,
            Ppid = snapshot.Ppid,
        };

        /// <summary>In-place update from a snapshot for fields known to mutate at runtime.</summary>
        public void ApplySnapshot(ProcessSnapshot s)
        {
            Name = s.Name;
            ThreadCount = s.ThreadCount;
            Priority = s.BasePriority;
            Ppid = s.Ppid;
        }

        /// <summary>Detached copy safe to hold across refreshes (export snapshots).</summary>
        public Process DeepCopy() => new()
        {
            Name = Name,
            Pid = Pid,
            Path = Path,
            ArchitectureType = ArchitectureType,
            Priority = Priority,
            ThreadCount = ThreadCount,
            Ppid = Ppid,
        };

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
