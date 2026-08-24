namespace TaskManager.Domain.Abstractions
{
    public interface IErrorHandler
    {
        void Handle(Exception exception, string operationContext);
        bool HandleDispatcherException(Exception exception);
        void LogFatal(Exception exception);
        void LogUnobserved(Exception exception);
    }
}
