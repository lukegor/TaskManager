using System.Security.Principal;
using TaskManager.Abstractions;

namespace TaskManager.Services
{
    internal sealed class ElevationService : IElevationService
    {
        public bool IsAdministrator { get; } =
            new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
    }
}
