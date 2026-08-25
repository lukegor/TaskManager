namespace TaskManager.Abstractions
{
    /// <summary>Elevation of the current process, evaluated once.</summary>
    public interface IElevationService
    {
        bool IsAdministrator { get; }
    }
}
