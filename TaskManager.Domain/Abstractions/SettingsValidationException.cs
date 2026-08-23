namespace TaskManager.Domain.Abstractions
{
    /// <summary>Rejected by <see cref="ISettingsService.Update"/> because the candidate failed validation.</summary>
    public sealed class SettingsValidationException : Exception
    {
        public SettingsValidationException(string message) : base(message)
        {
        }
    }
}
