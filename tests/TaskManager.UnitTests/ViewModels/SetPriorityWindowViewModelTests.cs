using NSubstitute;
using System.Diagnostics;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Resources.Languages;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    /// <summary>
    /// Confirm-flow contract: missing selection alerts without closing; success applies via
    /// catalog, marks Confirmed, raises RequestClose; partial failures report formatted data.
    /// </summary>
    public class SetPriorityWindowViewModelTests
    {
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private static readonly IReadOnlyCollection<int> Pids = [7, 8];

        private SetPriorityWindowViewModel CreateViewModel() =>
            new(_messages, _catalog, Pids, _errorHandler);

        [Fact]
        public void Confirm_NoPriorityChosen_ShowsError_DoesNotApplyOrClose()
        {
            var vm = CreateViewModel();
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                Strings.Select, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            _catalog.DidNotReceive().SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>());
            closed.ShouldBeFalse();
            vm.Confirmed.ShouldBeFalse();
        }

        [Fact]
        public void Confirm_AppliesViaCatalog_MarksConfirmed_RaisesRequestClose()
        {
            _catalog.SetPriority(Pids, ProcessPriorityClass.AboveNormal).Returns(ProcessOpSummary.Empty);
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.AboveNormal;
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            _catalog.Received(1).SetPriority(Pids, ProcessPriorityClass.AboveNormal);
            vm.Confirmed.ShouldBeTrue();
            closed.ShouldBeTrue();
        }

        [Fact]
        public void Confirm_PartialFailures_ReportsFormattedSummary_BeforeClosing()
        {
            _catalog.SetPriority(Pids, ProcessPriorityClass.High).Returns(new ProcessOpSummary
            {
                SucceededPids = [7],
                Failures = [new ProcessOpFailure(8, ProcessOpFailureReason.ProcessExited)]
            });
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.High;

            vm.OnConfirmCommand.Execute(null);

            _messages.Received(1).ShowMessage(
                string.Format(Strings.OpsCompletedWithFailuresFormat, 1, 2),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
            vm.Confirmed.ShouldBeTrue(); // batch survived; dialog still closes
        }

        [Fact]
        public void Confirm_CatalogThrows_IsGuarded_DoesNotClose()
        {
            _catalog.When(c => c.SetPriority(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>()))
                .Do(_ => throw new InvalidOperationException("os exploded"));
            var vm = CreateViewModel();
            vm.Priority = ProcessPriorityClass.Normal;
            var closed = false;
            vm.RequestClose += (_, _) => closed = true;

            vm.OnConfirmCommand.Execute(null);

            closed.ShouldBeFalse();
            vm.Confirmed.ShouldBeFalse();
        }
    }
}
