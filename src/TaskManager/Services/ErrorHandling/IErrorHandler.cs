namespace TaskManager.Services.ErrorHandling
{
    /// <summary>
    /// Last-stop handler for exceptions that are not part of any modeled outcome.
    /// </summary>
    public interface IErrorHandler
    {
        /// <summary>Logs the failure with context and informs the user; execution continues afterwards.</summary>
        void Handle(Exception exception, string operationContext);

        /// <summary>
        /// Handles an exception that reached the WPF dispatcher unobserved.
        /// Returns true when the user chose to keep the application running.
        /// </summary>
        bool HandleDispatcherException(Exception exception);

        /// <summary>Records a fatal exception from which the runtime cannot recover.</summary>
        void LogFatal(Exception exception);

        /// <summary>Records an unobserved faulted task (non-fatal by design).</summary>
        void LogUnobserved(Exception exception);
    }
}
