using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Primitives;

namespace TaskManager.Domain.Services
{
    public readonly record struct ProcessEnrichment(string Path, ArchitectureType Architecture);

    /// <summary>
    /// Expensive per-PID work requiring a handle: image path and WOW64 bitness.
    /// Stateless — caching (once per PID, both values immutable while running) is the caller's job.
    /// </summary>
    public class ProcessEnricher(ILogger<ProcessEnricher> logger)
    {
        public virtual bool TryEnrich(int pid, out ProcessEnrichment enrichment)
        {
            enrichment = default;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                bool? is32Bit = null;
                if (TryGetProcessBitness(process.Handle, out bool wow64))
                {
                    is32Bit = wow64;
                }

                enrichment = new ProcessEnrichment(
                    process.MainModule?.FileName ?? string.Empty,
                    is32Bit switch
                    {
                        true => ArchitectureType.Bit32,
                        false => ArchitectureType.Bit64,
                        null => ArchitectureType.Unknown
                    });
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(ex, "Enrichment failed for PID {Pid}; keeping snapshot-level data", pid);
                }
                enrichment = new ProcessEnrichment(string.Empty, ArchitectureType.Unknown);
                return false;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(nint hProcess, out bool wow64Process);

        private bool TryGetProcessBitness(nint processHandle, out bool is32Bit)
        {
            if (IsWow64Process(processHandle, out is32Bit)) { return true; }

            int errorCode = Marshal.GetLastWin32Error();
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("IsWow64Process failed for handle {Handle}. Error code: {ErrorCode}", processHandle, errorCode);
            }
            return false;
        }
    }
}
