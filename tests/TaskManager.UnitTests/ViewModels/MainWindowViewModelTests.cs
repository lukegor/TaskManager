using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using System.Collections.ObjectModel;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Resources.Languages;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    /// <summary>
    /// Headless main-window flows: selection precondition, terminate confirmation gate,
    /// partial-failure reporting, delegation of dialogs/snapshot to services.
    /// </summary>
    public class MainWindowViewModelTests
    {
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IWindowService _windows = Substitute.For<IWindowService>();

        public MainWindowViewModelTests()
        {
            _catalog.Items.Returns(new ReadOnlyObservableCollection<ProcessItem>(new ObservableCollection<ProcessItem>()));
        }

        private MainWindowViewModel CreateViewModel() =>
            new(_messages, _catalog, _errorHandler, _settings, _windows);

        private static ProcessItem Row(int pid, bool selected = false) =>
            new(new Process { Name = $"p{pid}", Pid = pid, Path = string.Empty }) { IsSelected = selected };

        private void SeedRows(params ProcessItem[] rows) =>
            _catalog.Items.Returns(new ReadOnlyObservableCollection<ProcessItem>(new ObservableCollection<ProcessItem>(rows)));

        [Fact]
        public void Constructor_DoesNotTouchCatalogSideEffects()
        {
            CreateViewModel();

            _catalog.DidNotReceiveWithAnyArgs().InitializeAsync();
        }

        [Fact]
        public async Task InitializeCommand_LoadsCatalogExactlyOnce()
        {
            await CreateViewModel().InitializeCommand.ExecuteAsync(null);

            await _catalog.Received(1).InitializeAsync();
        }

        [Fact]
        public async Task Terminate_NoSelection_ShowsAlert_AndNeverTouchesCatalogOrConfirm()
        {
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _messages.Received(1).ShowMessage(
                Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _catalog.DidNotReceive().TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public async Task Terminate_UserCancelsConfirmation_OperationSkipped()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.Cancel);
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _catalog.DidNotReceive().TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public async Task Terminate_Confirmed_CleanSummary_NoExtraMessages()
        {
            SeedRows(Row(1, selected: true), Row(2, selected: false));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>()).Returns(ProcessOpSummary.Empty);
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _catalog.Received(1).TerminateProcessesAsync(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.Single() == 1)); // unselected row excluded
            _messages.ReceivedCalls().Count().ShouldBe(1); // confirmation prompt only
        }

        [Fact]
        public async Task Terminate_PartialFailures_ReportsFormattedSummary()
        {
            SeedRows(Row(1, selected: true));
            _messages
                .ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                .Returns(MessageBoxResult.OK);
            _catalog.TerminateProcessesAsync(Arg.Any<IReadOnlyCollection<int>>()).Returns(new ProcessOpSummary
            {
                SucceededPids = [],
                Failures = [new ProcessOpFailure(1, ProcessOpFailureReason.AccessDenied)]
            });
            var vm = CreateViewModel();

            await vm.TerminateCommand.ExecuteAsync(null);

            _messages.Received(1).ShowMessage(
                string.Format(Strings.OpsCompletedWithFailuresFormat, 0, 1),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [Fact]
        public void Export_PassesMaterializedSnapshot_ToWindowService()
        {
            var snapshot = new List<Process> { new() { Name = "p1", Pid = 1, Path = string.Empty } };
            _catalog.SnapshotForExport().Returns(snapshot);
            var vm = CreateViewModel();

            vm.ExportCommand.Execute(null);

            _windows.Received(1).ShowExport(Arg.Is<IReadOnlyList<Process>>(p => p.Single().Pid == 1));
        }

        [Fact]
        public async Task SetPriority_NoSelection_Alerts_AndDoesNotOpenDialog()
        {
            var vm = CreateViewModel();

            await vm.SetPriorityCommand.ExecuteAsync(null);

            _messages.Received(1).ShowMessage(
                Strings.SelectProcess, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _windows.DidNotReceive().ShowSetPriority(Arg.Any<IReadOnlyCollection<int>>());
        }

        [Fact]
        public async Task SetPriority_Selected_DelegatesSelectedPidsToWindowService()
        {
            SeedRows(Row(5, selected: true), Row(6, selected: true));
            var vm = CreateViewModel();

            await vm.SetPriorityCommand.ExecuteAsync(null);

            _windows.Received(1).ShowSetPriority(
                Arg.Is<IReadOnlyCollection<int>>(pids => pids.OrderBy(x => x).SequenceEqual(new[] { 5, 6 })));
        }
    }
}
