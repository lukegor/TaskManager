using System.Runtime.CompilerServices;
using TaskManager.Domain.Abstractions;

namespace TaskManager.Services.ErrorHandling
{
    public static class ErrorHandlerExtensions
    {
        public static void Guard(this IErrorHandler errorHandler, Action operation,
            [CallerMemberName] string operationContext = "")
        {
            try
            {
                operation();
            }
            catch (Exception ex)
            {
                errorHandler.Handle(ex, operationContext);
            }
        }

        public static async Task GuardAsync(this IErrorHandler errorHandler, Func<Task> operation,
            [CallerMemberName] string operationContext = "")
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                errorHandler.Handle(ex, operationContext);
            }
        }

        public static T Guard<T>(this IErrorHandler errorHandler, Func<T> operation,
            [CallerMemberName] string operationContext = "")
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                errorHandler.Handle(ex, operationContext);
                return default!;
            }
        }
    }
}
