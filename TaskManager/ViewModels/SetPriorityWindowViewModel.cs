﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Task_Manager.UI.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TaskManager.Domain.Services;
using TaskManager.Utility.Utility;
using TaskManager.Shared.Resources.Languages;

namespace Task_Manager.ViewModels
{
    internal class SetPriorityWindowViewModel : ObservableObject
    {
        public IList<string> Priorities { get; } = PriorityTypeHelper.GetAllLocalized().ToList();

        private ProcessPriorityClass? _priority = null;
        public ProcessPriorityClass? Priority
        {
            get { return _priority; }
            set { SetProperty(ref _priority, value); }
        }

        public ICommand OnConfirmCommand { get; private set; }

        public ProcessManager ProcessManager { get; set; }

        public SetPriorityWindowViewModel()
        {
            OnConfirmCommand = new RelayCommand(OnConfirm);
        }

        public IEnumerable<System.Diagnostics.Process> Processes { get; set; }

        public SetPriorityWindowViewModel(IEnumerable<System.Diagnostics.Process> processes) : base()
        {
            this.Processes = processes;
        }

        private void OnConfirm()
        {
            if (Priority == null)
            {
                MessageBox.Show("Select", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ProcessManager.SetPriority(Processes, (ProcessPriorityClass)Priority);

            var window = App.Current.Windows.OfType<SetPriorityWindow>().FirstOrDefault(x => x.DataContext == this);
            window?.Close();
        }
    }
}
