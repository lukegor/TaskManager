using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.Services.ErrorHandling;
using TaskManager.UI.Localization;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for SetPriorityWindow. Applies the chosen priority through the catalog,
    /// reports partial failures as data, then raises RequestClose (host window closes).
    /// </summary>
    internal class SetPriorityWindowViewModel : ObservableObject, IRequestCloseObservable
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();

        public ProcessPriorityClass? Priority { get; set => SetProperty(ref field, value); }

        public AsyncRelayCommand OnConfirmCommand { get; }

        public event EventHandler? RequestClose;

        public bool Confirmed { get; private set; }

        private readonly IMessageService _messageService;
        private readonly IProcessListCatalog _catalog;
        private readonly IErrorHandler _errorHandler;
        private readonly IReadOnlyCollection<int> _processIds;

        public SetPriorityWindowViewModel(IMessageService messageService, IProcessListCatalog catalog,
            IReadOnlyCollection<int> processes, IErrorHandler errorHandler)
        {
            _messageService = messageService;
            _catalog = catalog;
            _processIds = processes;
            _errorHandler = errorHandler;
            OnConfirmCommand = new AsyncRelayCommand(OnConfirmAsync);
        }

        private async Task OnConfirmAsync()
        {
            await _errorHandler.GuardAsync(async () =>
            {
                if (Priority == null)
                {
                    _messageService.ShowMessage(Strings.Select, Strings.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var summary = await _catalog.SetPriorityAsync(_processIds, (ProcessPriorityClass)Priority);
                ReportPartialFailures(summary);

                Confirmed = true;
                RequestClose?.Invoke(this, EventArgs.Empty);
            }, "applying the selected priority");
        }

        private void ReportPartialFailures(ProcessOpSummary summary)
        {
            if (!summary.HasFailures)
            {
                return;
            }

            _messageService.ShowMessage(OperationSummaryReporter.FormatPartialFailures(summary),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
