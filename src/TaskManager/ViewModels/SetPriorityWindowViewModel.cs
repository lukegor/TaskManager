using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.Resources.Languages;
using TaskManager.UI.Views;
using TaskManager.Domain.Primitives;
using TaskManager.UI.Localization;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="SetPriorityWindow"/>
    /// </summary>
    internal class SetPriorityWindowViewModel : ViewModelBase
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();

        public ProcessPriorityClass? Priority { get; set => SetProperty(ref field, value); }

        public ICommand OnConfirmCommand { get; }

        private readonly IMessageService _messageService;
        private readonly ProcessManager _processManager;
        private readonly IErrorHandler _errorHandler;

        private readonly IReadOnlyCollection<int> _processIds;

        public SetPriorityWindowViewModel(IMessageService messageService, ProcessManager processManager,
            IReadOnlyCollection<int> processes, IErrorHandler errorHandler)
        {
            _messageService = messageService;
            _processManager = processManager;
            _processIds = processes;
            _errorHandler = errorHandler;
            OnConfirmCommand = new RelayCommand(OnConfirm);
        }

        private void OnConfirm()
        {
            _errorHandler.Guard(() =>
            {
                if (Priority == null)
                {
                    _messageService.ShowMessage(Strings.Select, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var summary = _processManager.SetPriority(_processIds, (ProcessPriorityClass)Priority);
                ReportPartialFailures(summary);

                var window = GetAssociatedWindow<SetPriorityWindow>();
                window?.Close();
            }, "applying the selected priority");
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            var total = summary.SucceededPids.Count + summary.Failures.Count;
            _messageService.ShowMessage(
                string.Format(Strings.OpsCompletedWithFailuresFormat, summary.SucceededPids.Count, total),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
