using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using Process = TaskManager.Domain.Models.Process;

namespace TaskManager.Abstractions
{
    /// <summary>
    /// Read side + batch operations facade over the live process list.
    /// Implementations raise <see cref="INotifyPropertyChanged"/> for <see cref="ProcessCount"/>.
    /// </summary>
    public interface IProcessListCatalog : INotifyPropertyChanged
    {
        ReadOnlyObservableCollection<ProcessItem> Items { get; }
        int ProcessCount { get; }

        /// <summary>Initial fill followed by polling start; call exactly once at startup.</summary>
        Task InitializeAsync();

        /// <summary>Polling skips when busy; user-initiated waits and restarts the polling phase.</summary>
        Task PerformRefreshAsync(bool isUserInitiated);

        /// <summary>Materialized deep copy of current rows; safe to hold across refreshes.</summary>
        IReadOnlyList<Process> SnapshotForExport();

        /// <summary>Delegates to ProcessOperationsService on a worker thread; expected OS failures are data, not exceptions.</summary>
        Task<ProcessOpSummary> TerminateProcessesAsync(IReadOnlyCollection<int> pids);

        /// <summary>
        /// Applies priority via ProcessOperationsService on a worker thread, then writes base
        /// priority back into stored rows on the caller's context (UI thread for commands).
        /// </summary>
        Task<ProcessOpSummary> SetPriorityAsync(IReadOnlyCollection<int> pids, ProcessPriorityClass priority);
    }
}
