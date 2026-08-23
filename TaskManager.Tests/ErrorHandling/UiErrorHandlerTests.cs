using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Services.ErrorHandling;

namespace TaskManager.Tests
{
    public class UiErrorHandlerTests
    {
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly UiErrorHandler _handler;

        public UiErrorHandlerTests()
        {
            _handler = new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, _messageService);
        }

        [Fact]
        public void Handle_ShowsExactlyOneDialog()
        {
            _handler.Handle(new InvalidOperationException("boom"), "testing");

            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void Handle_DoesNotThrowWhenReportingItselfFails()
        {
            _messageService
                .When(m => m.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<MessageBoxButton>(), Arg.Any<MessageBoxImage>()))
                .Do(_ => throw new InvalidOperationException("dialog failed"));

            _handler.Handle(new InvalidOperationException("original"), "testing");
        }

        [Fact]
        public void HandleDispatcherException_ReturnsUserChoice()
        {
            _messageService.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                MessageBoxButton.YesNo, MessageBoxImage.Error).Returns(MessageBoxResult.No);

            var keepsRunning = _handler.HandleDispatcherException(new InvalidOperationException("boom"));

            Assert.False(keepsRunning);
        }

        [Fact]
        public void HandleDispatcherException_DoesNotThrowWhenReportingItselfFails()
        {
            _messageService.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<MessageBoxButton>(), Arg.Any<MessageBoxImage>())
                .Returns(_ => throw new InvalidOperationException("dialog failed"));

            var keepsRunning = _handler.HandleDispatcherException(new InvalidOperationException("boom"));

            Assert.False(keepsRunning);
        }
    }
}
