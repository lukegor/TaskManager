using System.Diagnostics;
using System.Globalization;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using TaskManager.Infrastructure;

namespace TaskManager.UiAutomationTests
{
    /// <summary>
    /// One real app process for the entire run, fully isolated from the developer's
    /// machine data via environment redirects. Owns automation lifetime and teardown.
    /// </summary>
    public sealed class AppSession : IDisposable
    {
        public const string MainWindowTitle = "Task Manager";

        private readonly string _settingsDir;
        private readonly string _logDir;
        private bool _disposed;

        public Application App { get; }
        public UIA3Automation Automation { get; }
        public string ExePath { get; }
        public string InstanceName { get; }
        public int ProcessId { get; }

        public AppSession()
        {
            var appAssembly = typeof(global::TaskManager.App).Assembly.Location;
            ExePath = Path.ChangeExtension(appAssembly, ".exe");
            _settingsDir = Path.Combine(Path.GetTempPath(), $"tm-uitest-settings-{Guid.NewGuid():N}");
            _logDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-test-logs");
            InstanceName = $"uitest-{Guid.NewGuid():N}";

            // Deterministic locale: seed English settings BEFORE launch so menu labels
            // match Strings.* values the tests assert against (a Polish-OS machine
            // would otherwise boot a Polish UI and every ByName lookup would miss).
            Directory.CreateDirectory(_settingsDir);
            File.WriteAllText(
                Path.Combine(_settingsDir, "settings.json"),
                "{\"Version\":1,\"Settings\":{\"Language\":\"English\",\"ProcessesRefreshFrequency\":\"Low\",\"DateTimeFormat\":\"yyyy_MM_dd--HH_mm_ss\"}}");

            // Pin the TEST host to the same locale so Strings.* resolves identically.
            var english = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            CultureInfo.DefaultThreadCurrentCulture = english;

            // Belt-and-braces: guarantee the child inherits the redirects even if a
            // launch path drops per-PSI environment overrides.
            Environment.SetEnvironmentVariable(TaskManagerEnvironment.SettingsDir, _settingsDir);
            Environment.SetEnvironmentVariable(TaskManagerEnvironment.LogDir, _logDir);
            Environment.SetEnvironmentVariable(TaskManagerEnvironment.InstanceName, InstanceName);

            var startInfo = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = false,
            };
            startInfo.EnvironmentVariables[TaskManagerEnvironment.SettingsDir] = _settingsDir;
            startInfo.EnvironmentVariables[TaskManagerEnvironment.LogDir] = _logDir;
            startInfo.EnvironmentVariables[TaskManagerEnvironment.InstanceName] = InstanceName;
            startInfo.EnvironmentVariables["TASKMANAGER_UITEST"] = "1";

            // A hard-killed predecessor leaves its logs behind; a stale dir would make
            // the redirect proof pass vacuously. Clean slate before launch.
            try
            {
                if (Directory.Exists(_logDir))
                {
                    Directory.Delete(_logDir, recursive: true);
                }
            }
            catch
            {
                // if deletion fails the proof may be weaker this run - not fatal
            }

            // FlaUI 5.0's Application.Launch(psi) drops psi.EnvironmentVariables
            // (verified empirically: redirected log dirs never appeared). Launch via
            // plain Process.Start - guaranteed env delivery - and attach FlaUI by PID.
            using var startedProcess = Process.Start(startInfo)!;
            ProcessId = startedProcess.Id;
            var automation = new UIA3Automation();
            var app = Application.Attach(startedProcess.Id);

            try
            {
                App = app;
                Automation = automation;
                InitializeSession();
            }
            catch
            {
                try { app.Kill(); } catch { /* already gone */ }
                try { automation.Dispose(); } catch { }
                throw;
            }
        }

        private void InitializeSession()
        {
            // A bounced launch (guard misfire / startup crash) must fail loudly,
            // not silently automate some unrelated process.
            var window = Retry.WhileNull(
                () => App.GetMainWindow(Automation, TimeSpan.FromMilliseconds(250)),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(250)).Result;

            window.ShouldNotBeNull("main window did not appear within 10 s");
            window.Properties.ProcessId.Value.ShouldBe(
                ProcessId,
                "attached window belongs to another instance - isolation is broken");

            // Redirect proof: the app's STARTUP MARKER log lands in OUR directory
            // within moments of launch (settings.json is written lazily on Save).
            Retry.WhileFalse(
                () => Directory.Exists(_logDir) && Directory.EnumerateFiles(_logDir).Any(),
                TimeSpan.FromSeconds(10)).Result.ShouldBeTrue(
                "app did not write logs into the redirected directory");
        }

        /// <summary>Finds a top-level window of the app by exact title (dialogs included).</summary>
        public AutomationElement? GetTopLevelWindow(string title)
        {
            var found = Retry.WhileNull(
                () => App.GetAllTopLevelWindows(Automation)
                        .FirstOrDefault(w => w.Title == title),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200));
            return found.Result;
        }

        public void AssertAlive() =>
            App.HasExited.ShouldBeFalse("the app exited unexpectedly");

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { App.Close(); } catch { /* already gone */ }
            try { App.Kill(); } catch { /* already gone */ }
            Automation.Dispose();

            try
            {
                if (Directory.Exists(_logDir))
                {
                    Directory.Delete(_logDir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
