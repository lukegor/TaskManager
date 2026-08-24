using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Models.Process wrapper with wider logic.
    /// <see cref="IsSelected"/> is the single source of truth for row selection:
    /// both the DataGrid row style and the select-checkbox bind to it TwoWay.
    /// </summary>
    public partial class ProcessItem : ObservableObject
    {
        public required Process Process { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [SetsRequiredMembers]
        public ProcessItem(Process process)
        {
            Process = process;
        }

        public override string ToString()
        {
            return $"{Process.Name} ({Process.Pid})";
        }
    }
}
