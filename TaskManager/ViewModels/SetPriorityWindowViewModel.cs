using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services;
using TaskManager.Shared.Resources.Languages;
using TaskManager.UI.Views;
using TaskManager.Utility.Utility;
using TaskManager.ViewModels.Abstraction;

namespace TaskManager.ViewModels
{
    /// <summary>
    /// Viewmodel for <see cref="SetPriorityWindow"/>
    /// </summary>
    internal class SetPriorityWindowViewModel : ViewModelBase
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();

        private ProcessPriorityClass? _priority = null;
        public ProcessPriorityClass? Priority
        {
            get { return _priority; }
            set { SetProperty(ref _priority, value); }
        }

        public ICommand OnConfirmCommand { get; private set; }

        private readonly IMessageService _messageService;
        private readonly ProcessManager _processManager;

        private readonly IEnumerable<int> _processIds;

        private SetPriorityWindowViewModel()
        {
            OnConfirmCommand = new RelayCommand(OnConfirm);
        }

        public SetPriorityWindowViewModel(IMessageService messageService, ProcessManager processManager, IEnumerable<int> processes) : this()
        {
            _messageService = messageService;
            _processManager = processManager;
            this._processIds = processes;
        }

        private void OnConfirm()
        {
            if (Priority == null)
            {
                _messageService.ShowMessage(Strings.Select, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _processManager.SetPriority(_processIds, (ProcessPriorityClass)Priority);

            var window = GetAssociatedWindow<SetPriorityWindow>();
            window?.Close();
        }
    }
}
