using System.Diagnostics;
using System.IO;

namespace TaskManager.UiAutomationTests
{
    /// <summary>Spawns a uniquely named, invisible, safely killable cmd.exe copy.</summary>
    public sealed class VictimFactory : IDisposable
    {
        private readonly List<Process> _victims = [];

        public Victim Spawn()
        {
            var name = $"tmuitest-{Guid.NewGuid():N}.exe";
            var target = Path.Combine(Path.GetTempPath(), name);
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), target, overwrite: true);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = "/c ping -n 60 127.0.0.1 > nul",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            _victims.Add(process);
            return new Victim(process, name);
        }

        public void Dispose()
        {
            foreach (var victim in _victims)
            {
                try { if (!victim.HasExited) { victim.Kill(); } }
                catch { /* best-effort */ }
                victim.Dispose();
            }
        }
    }

    public sealed record Victim(Process Process, string DisplayName)
    {
        public int Id => Process.Id;
        public string RowSearchText => DisplayName; // grid row name aggregates the exe file name
        public bool HasExited => Process.HasExited;
        public void WaitUntilExited(TimeSpan timeout) =>
            Process.WaitForExit((int)timeout.TotalMilliseconds).ShouldBeTrue("victim refused to die");
    }
}
