using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Models.Process wrapper with wider logic.
    /// <see cref="IsSelected"/> is the single source of truth for row selection:
    /// both the DataGrid row style and the select-checkbox bind to it TwoWay.
    /// </summary>
    public class ProcessItem : INotifyPropertyChanged
    {
        public required Process Process { get; set; }

        public bool IsSelected
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }

                field = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        [SetsRequiredMembers]
        public ProcessItem(Process process)
        {
            Process = process;
        }

        public override string ToString()
        {
            return $"{Process.Name} ({Process.Pid})";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
