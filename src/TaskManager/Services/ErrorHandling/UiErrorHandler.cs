using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Abstractions;
using TaskManager.Resources.Languages;

namespace TaskManager.Services.ErrorHandling
{
    public class UiErrorHandler : IErrorHandler
    {
        private readonly ILogger<UiErrorHandler> _logger;
        private readonly IMessageService _messageService;

        public UiErrorHandler(ILogger<UiErrorHandler> logger, IMessageService messageService)
        {
            _logger = logger;
            _messageService = messageService;
        }

        public void Handle(Exception exception, string operationContext)
        {
            try
            {
                _logger.LogError(exception, "Unexpected failure while {OperationContext}", operationContext);
                _messageService.ShowMessage(
                    string.Format(Strings.UnexpectedErrorFormat, operationContext, exception.Message),
                    Strings.Error,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // error reporting itself must never throw; Tier 3 remains the backstop
            }
        }

        public bool HandleDispatcherException(Exception exception)
        {
            try
            {
                _logger.LogError(exception, "Unhandled exception reached the WPF dispatcher");
                var choice = _messageService.ShowMessage(
                    string.Format(Strings.ContinueAfterErrorFormat, exception.Message),
                    Strings.Error,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                return choice == MessageBoxResult.Yes;
            }
            catch
            {
                return false;
            }
        }

        public void LogFatal(Exception exception)
        {
            try
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                _logger.LogCritical(exception, "FATAL unhandled exception. Application version {Version}", version);
                _messageService.ShowMessage(Strings.FatalErrorFormat, Strings.Error,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        public void LogUnobserved(Exception exception)
        {
            try
            {
                _logger.LogWarning(exception, "Unobserved task exception");
            }
            catch
            {
            }
        }
    }
}
