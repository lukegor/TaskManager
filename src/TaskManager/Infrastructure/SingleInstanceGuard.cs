using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TaskManager.Infrastructure
{
    /// <summary>
    /// Per-session single instancing via a named mutex. Normal mode decides instantly
    /// (createdNew); handoff mode (restart/relaunch successors carrying --await-instance)
    /// waits up to 10 s for the predecessor to release. Activation brings the first
    /// instance's window to the foreground.
    /// </summary>
    internal sealed partial class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = "Local\\TaskManager.SingleInstance";
        private const int RestoreCommand = 9; // SW_RESTORE

        private Mutex? _mutex;

        private readonly string _mutexName;

        public SingleInstanceGuard(bool waitForExistingRelease, string? instanceName = null)
        {
            _mutexName = string.IsNullOrEmpty(instanceName)
                ? MutexName
                : $"{MutexName}.{instanceName}";

            if (!waitForExistingRelease)
            {
                _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
                IsFirstInstance = createdNew;
                if (!IsFirstInstance)
                {
                    _mutex.Dispose();
                    _mutex = null;
                }

                return;
            }

            // Handoff: attach to the existing mutex and wait for the predecessor to release.
            _ = Mutex.TryOpenExisting(_mutexName, out var existing);
            if (existing is null)
            {
                // predecessor exited before we attached: we are the successor owner.
                _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
                IsFirstInstance = createdNew;
                if (!IsFirstInstance)
                {
                    _mutex.Dispose();
                    _mutex = null;
                }

                return;
            }

            try
            {
                IsFirstInstance = existing.WaitOne(TimeSpan.FromSeconds(10));
                if (IsFirstInstance)
                {
                    _mutex = existing;
                }
                else
                {
                    existing.Dispose(); // timeout: behave as second instance
                }
            }
            catch (AbandonedMutexException)
            {
                // predecessor crashed while holding: ownership transferred to us.
                IsFirstInstance = true;
                _mutex = existing;
            }
        }

        public bool IsFirstInstance { get; private set; }

        public void ActivateFirstInstanceWindow()
        {
            var currentId = Environment.ProcessId;
            var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);

            foreach (var process in Process.GetProcessesByName(name))
            {
                using var ownedProcess = process;
                if (process.Id == currentId || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (IsIconic(process.MainWindowHandle))
                {
                    ShowWindow(process.MainWindowHandle, RestoreCommand);
                }

                _ = SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }

        public void Dispose()
        {
            if (_mutex is null)
            {
                return;
            }

            // Handle disposal alone does NOT surrender ownership (Win32 ties it to
            // the owning thread); release explicitly so a waiting successor proceeds.
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // not the owning thread: nothing to release
            }

            _mutex.Dispose();
            _mutex = null;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsIconic(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
