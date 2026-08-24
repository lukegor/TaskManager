using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Represents Windows System Process data (core business logic).
    /// Raises per-field change notifications so bound grid rows update in place.
    /// </summary>
    public class Process : IExportable, INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;
        private int? _pid;
        private ArchitectureType _architectureType;
        private int? _priority;
        private int _threadCount;
        private int? _ppid;

        public event PropertyChangedEventHandler? PropertyChanged;

        public required string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public int? Pid
        {
            get => _pid;
            set => SetField(ref _pid, value);
        }

        [IgnoreSerialization]
        [JsonIgnore]
        public ArchitectureType ArchitectureType
        {
            get => _architectureType;
            set
            {
                if (_architectureType == value)
                {
                    return;
                }

                _architectureType = value;
                Notify(nameof(ArchitectureType));
                Notify(nameof(ArchitectureTypeDisplay));
            }
        }

        public required string Path
        {
            get => _path;
            set => SetField(ref _path, value);
        }

        public int? Priority
        {
            get => _priority;
            set => SetField(ref _priority, value);
        }

        public int ThreadCount
        {
            get => _threadCount;
            set => SetField(ref _threadCount, value);
        }

        public int? Ppid
        {
            get => _ppid;
            set => SetField(ref _ppid, value);
        }

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

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            Notify(propertyName!);
        }

        private void Notify(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
