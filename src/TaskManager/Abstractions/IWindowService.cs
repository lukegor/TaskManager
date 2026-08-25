using TaskManager.Domain.Models;

namespace TaskManager.Abstractions
{
    /// <summary>Window orchestration owned by the composition root. Message boxes stay in IMessageService.</summary>
    public interface IWindowService
    {
        /// <summary>Opens the settings dialog modally; returns after it closes.</summary>
        void ShowSettings();

        /// <summary>Opens the data-export dialog seeded with a materialized process snapshot.</summary>
        void ShowExport(IReadOnlyList<Process> processes);

        /// <summary>Opens the priority dialog for the given PIDs. True = user confirmed and the operation applied.</summary>
        bool ShowSetPriority(IReadOnlyCollection<int> pids);

        /// <summary>Opens the about dialog modally.</summary>
        void ShowAbout();
    }
}
