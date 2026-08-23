# Exception Handling Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved three-tier exception handling design (see `docs/superpowers/specs/2026-08-23-exception-handling-optimization-design.md`) so expected OS failures become data, unexpected failures are logged and reported without crashing, and every known crash vector is fixed.

**Architecture:** Three defense tiers — Domain returns outcome summaries instead of throwing for expected failures (Tier 1), a reusable `IErrorHandler` guard catches surprises in ViewModel commands (Tier 2), and global handlers in `App` are the last-resort safety net (Tier 3). A rolling file logger (`Microsoft.Extensions.Logging` + in-repo sink) makes every handled failure visible.

**Tech Stack:** .NET 8 WPF, Microsoft.Extensions.DependencyInjection 9.0.4, Microsoft.Extensions.Logging(.Abstractions) 9.0.4, CommunityToolkit.Mvvm 8.4.0, xUnit v3 + NSubstitute.

## Global Constraints

- All projects target `net8.0-windows`, `Nullable` enabled.
- New package versions must be exactly `9.0.4` (matches existing `Microsoft.Extensions.DependencyInjection 9.0.4`).
- **No custom exception classes anywhere** (spec §3.3).
- Domain projects must not take WPF UI dependencies; logging via abstractions only (`Microsoft.Extensions.Logging.Abstractions`).
- User-facing text goes through `TaskManager.Shared/Resources/Languages/Strings.resx` + generated accessor `Strings.Designer.cs`. New keys are added to the neutral resx only; Polish localization is an optional follow-up (missing keys fall back to neutral automatically).
- Build/test validation for every task: `dotnet build TaskManager.sln` then `dotnet test TaskManager.Tests/TaskManager.Tests.csproj`.
- Test conventions: xUnit v3 (`[Fact]` plain — no dispatcher needed in this plan's tests), NSubstitute for abstractions, `NullLogger<T>.Instance` where a logger is required but not under test. Tests live under `TaskManager.Tests/` with namespace `TaskManager.Tests`.
- Commit messages follow repo convention (conventional commits: `feat(scope):`, `fix(scope):`, `refactor(scope):`, `test:`).
- The `TaskManager` csproj already declares `<InternalsVisibleTo Include="TaskManager.Tests"/>`; internal types are testable as-is.

---

### Task 1: Logging Infrastructure (FileLoggerProvider + DI wiring)

**Files:**
- Modify: `TaskManager/TaskManager.csproj` (add package)
- Modify: `TaskManager.Domain/TaskManager.Domain.csproj` (add package)
- Create: `TaskManager/Infrastructure/Logging/FileLoggerProvider.cs`
- Modify: `TaskManager/App.xaml.cs` (register logging)
- Test: `TaskManager.Tests/Logging/FileLoggerProviderTests.cs`

**Interfaces:**
- Consumes: nothing (foundation).
- Produces: `TaskManager.Infrastructure.Logging.FileLoggerProvider` — public sealed class implementing `ILoggerProvider`; ctors `FileLoggerProvider()` (writes under `%LOCALAPPDATA%\TaskManager\logs`) and `FileLoggerProvider(string logDirectory)`. All later tasks inject `ILogger<T>` normally.

- [ ] **Step 1: Add package references**

In `TaskManager/TaskManager.csproj`, next to the existing `Microsoft.Extensions.DependencyInjection` reference (line ~32):

```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.4" />
```

In `TaskManager.Domain/TaskManager.Domain.csproj`, next to the existing package references (line ~10):

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.4" />
```

- [ ] **Step 2: Create FileLoggerProvider**

Create `TaskManager/Infrastructure/Logging/FileLoggerProvider.cs` (match repo style: classic properties/ctors, no primary constructors):

```csharp
using Microsoft.Extensions.Logging;

namespace TaskManager.Infrastructure.Logging
{
    /// <summary>
    /// Minimal daily-rolling file logger. One file per day inside the log directory,
    /// retention-limited on construction. Every write is appended under a lock,
    /// so no explicit flush is needed anywhere.
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private const int RetentionDays = 7;
        private static readonly string DefaultLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskManager",
            "logs");

        private readonly string _logDirectory;
        private readonly object _writeLock = new();

        public FileLoggerProvider() : this(DefaultLogDirectory)
        {
        }

        public FileLoggerProvider(string logDirectory)
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(_logDirectory);
            DeleteExpiredLogs();
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(this, categoryName);
        }

        public void Dispose()
        {
        }

        internal void Write(LogLevel logLevel, string category, string? message, Exception? exception)
        {
            var fileName = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_writeLock)
            {
                File.AppendAllText(fileName, line + Environment.NewLine);
            }
        }

        private void DeleteExpiredLogs()
        {
            foreach (var filePath in Directory.GetFiles(_logDirectory, "tm-*.log"))
            {
                if (File.GetLastWriteTime(filePath) < DateTime.Now.AddDays(-RetentionDays))
                {
                    File.Delete(filePath);
                }
            }
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _owner;
            private readonly string _category;

            public FileLogger(FileLoggerProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _owner.Write(logLevel, _category, formatter(state, exception), exception);
            }
        }
    }
}
```

- [ ] **Step 3: Register logging in DI**

Modify `TaskManager/App.xaml.cs`: add usings and register at the top of `ConfigureServices(IServiceCollection services)`:

```csharp
using Microsoft.Extensions.Logging;
using TaskManager.Infrastructure.Logging;
```

```csharp
private void ConfigureServices(IServiceCollection services)
{
    services.AddLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddProvider(new FileLoggerProvider());
    });

    // Register Services
    services.AddSingleton<IAppSettings, SettingsService>();
    // ... rest unchanged
```

(`Debug` minimum level is deliberate — spec §3.1 requires per-process enumeration diagnostics to persist.)

- [ ] **Step 4: Add tests**

Create `TaskManager.Tests/Logging/FileLoggerProviderTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TaskManager.Infrastructure.Logging;

namespace TaskManager.Tests
{
    public class FileLoggerProviderTests : IDisposable
    {
        private readonly string _logDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-log-tests-{Guid.NewGuid():N}");

        [Fact]
        public void Write_AppendsFormattedMessageToDailyFile()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            var logger = provider.CreateLogger("Test.Category");

            logger.LogInformation("hello {Name}", "world");

            var file = Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(file));
            var content = File.ReadAllText(file);
            Assert.Contains("hello world", content);
            Assert.Contains("[Information]", content);
            Assert.Contains("Test.Category", content);
        }

        [Fact]
        public void Write_IncludesExceptionDetails()
        {
            using var provider = new FileLoggerProvider(_logDirectory);
            var logger = provider.CreateLogger("Cat");

            logger.LogError(new InvalidOperationException("boom"), "op failed");

            var content = File.ReadAllText(Path.Combine(_logDirectory, $"tm-{DateTime.Now:yyyyMMdd}.log"));
            Assert.Contains("boom", content);
            Assert.Contains("[Error]", content);
        }

        [Fact]
        public void Constructor_DeletesLogsOlderThanRetention()
        {
            Directory.CreateDirectory(_logDirectory);
            var staleLog = Path.Combine(_logDirectory, "tm-20200101.log");
            File.WriteAllText(staleLog, "old");
            File.SetLastWriteTime(staleLog, DateTime.Now.AddDays(-30));

            using var provider = new FileLoggerProvider(_logDirectory);

            Assert.False(File.Exists(staleLog));
        }

        public void Dispose()
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 5: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

Also launch the app once manually and confirm `%LOCALAPPDATA%\TaskManager\logs\tm-YYYYMMDD.log` is created (empty is fine).

- [ ] **Step 6: Commit**

```bash
git add TaskManager/TaskManager.csproj TaskManager.Domain/TaskManager.Domain.csproj TaskManager/Infrastructure/Logging/FileLoggerProvider.cs TaskManager/App.xaml.cs TaskManager.Tests/Logging/FileLoggerProviderTests.cs
git commit -m "feat(app): add rolling file logging infrastructure"
```

---

### Task 2: IErrorHandler, UiErrorHandler and Guard Extensions

**Files:**
- Create: `TaskManager/Services/ErrorHandling/IErrorHandler.cs`
- Create: `TaskManager/Services/ErrorHandling/UiErrorHandler.cs`
- Create: `TaskManager/Services/ErrorHandling/ErrorHandlerExtensions.cs`
- Modify: `TaskManager/App.xaml.cs` (DI registration only)
- Modify: `TaskManager.Shared/Resources/Languages/Strings.resx`
- Modify: `TaskManager.Shared/Resources/Languages/Strings.Designer.cs`
- Test: `TaskManager.Tests/ErrorHandling/UiErrorHandlerTests.cs`

**Interfaces:**
- Consumes: `ILogger<T>` (Task 1), `IMessageService` (existing, `TaskManager.Domain.Abstractions`), localized `Strings` (existing pattern).
- Produces (used by Tasks 3 and 7):
  - `TaskManager.Services.ErrorHandling.IErrorHandler`: `void Handle(Exception exception, string operationContext)`, `bool HandleDispatcherException(Exception exception)` (true = keep running), `void LogFatal(Exception exception)`, `void LogUnobserved(Exception exception)`.
  - `ErrorHandlerExtensions.Guard(this IErrorHandler, Action, [CallerMemberName] string operationContext = "")` and `GuardAsync(this IErrorHandler, Func<Task>, ...)`.
  - DI singleton registered as `IErrorHandler` → `UiErrorHandler`.

- [ ] **Step 1: Add localized strings**

Append to `TaskManager.Shared/Resources/Languages/Strings.resx` just before `</root>` (same format as existing entries):

```xml
  <data name="UnexpectedErrorFormat" xml:space="preserve">
    <value>An unexpected error occurred while {0}.

Details: {1}</value>
  </data>
  <data name="ContinueAfterErrorFormat" xml:space="preserve">
    <value>An unexpected error occurred.

{0}

Do you want to continue running the application?</value>
  </data>
  <data name="FatalErrorFormat" xml:space="preserve">
    <value>A fatal error occurred and the application must close.

Details were written to the log file.</value>
  </data>
```

Append to `TaskManager.Shared/Resources/Languages/Strings.Designer.cs` before the class's closing brace (match the generator's property pattern):

```csharp
        /// <summary>
        ///   Looks up a localized string similar to An unexpected error occurred while {0}....
        /// </summary>
        public static string UnexpectedErrorFormat {
            get {
                return ResourceManager.GetString("UnexpectedErrorFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to An unexpected error occurred....
        /// </summary>
        public static string ContinueAfterErrorFormat {
            get {
                return ResourceManager.GetString("ContinueAfterErrorFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to A fatal error occurred and the application must close.....
        /// </summary>
        public static string FatalErrorFormat {
            get {
                return ResourceManager.GetString("FatalErrorFormat", resourceCulture);
            }
        }
```

- [ ] **Step 2: Create IErrorHandler**

```csharp
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
```

- [ ] **Step 3: Create UiErrorHandler**

```csharp
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.Services.ErrorHandling
{
    public class UiErrorHandler : IErrorHandler
    {
        private readonly ILogger<UiErrorHandler> _logger;
        private readonly IMessageService _messageService;

        public UiErrorHandler(ILogger<UiErrorHandler> logger, IMessageService messageService)
        {
            _logger = logger;
            _messageService = messageService;
        }

        public void Handle(Exception exception, string operationContext)
        {
            try
            {
                _logger.LogError(exception, "Unexpected failure while {OperationContext}", operationContext);
                _messageService.ShowMessage(
                    string.Format(Strings.UnexpectedErrorFormat, operationContext, exception.Message),
                    Strings.Error,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // error reporting itself must never throw; Tier 3 remains the backstop
            }
        }

        public bool HandleDispatcherException(Exception exception)
        {
            try
            {
                _logger.LogError(exception, "Unhandled exception reached the WPF dispatcher");
                var choice = _messageService.ShowMessage(
                    string.Format(Strings.ContinueAfterErrorFormat, exception.Message),
                    Strings.Error,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                return choice == MessageBoxResult.Yes;
            }
            catch
            {
                return false;
            }
        }

        public void LogFatal(Exception exception)
        {
            try
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                _logger.LogCritical(exception, "FATAL unhandled exception. Application version {Version}", version);
                _messageService.ShowMessage(Strings.FatalErrorFormat, Strings.Error,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
            }
        }

        public void LogUnobserved(Exception exception)
        {
            try
            {
                _logger.LogWarning(exception, "Unobserved task exception");
            }
            catch
            {
            }
        }
    }
}
```

- [ ] **Step 4: Create Guard extensions**

```csharp
using System.Runtime.CompilerServices;

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
    }
}
```

- [ ] **Step 5: Register in DI**

Modify `TaskManager/App.xaml.cs`: add `using TaskManager.Services.ErrorHandling;` and register after the `IMessageService` registration in `ConfigureServices`:

```csharp
services.AddSingleton<IErrorHandler, UiErrorHandler>();
```

- [ ] **Step 6: Add tests**

Create `TaskManager.Tests/ErrorHandling/UiErrorHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Services.ErrorHandling;

namespace TaskManager.Tests
{
    public class UiErrorHandlerTests
    {
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly UiErrorHandler _handler;

        public UiErrorHandlerTests()
        {
            _handler = new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, _messageService);
        }

        [Fact]
        public void Handle_ShowsExactlyOneDialog()
        {
            _handler.Handle(new InvalidOperationException("boom"), "testing");

            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void Handle_DoesNotThrowWhenReportingItselfFails()
        {
            _messageService
                .When(m => m.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<MessageBoxButton>(), Arg.Any<MessageBoxImage>()))
                .Do(_ => throw new InvalidOperationException("dialog failed"));

            _handler.Handle(new InvalidOperationException("original"), "testing");
        }

        [Fact]
        public void HandleDispatcherException_ReturnsUserChoice()
        {
            _messageService.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                MessageBoxButton.YesNo, MessageBoxImage.Error).Returns(MessageBoxResult.No);

            var keepsRunning = _handler.HandleDispatcherException(new InvalidOperationException("boom"));

            Assert.False(keepsRunning);
        }

        [Fact]
        public void HandleDispatcherException_DoesNotThrowWhenReportingItselfFails()
        {
            _messageService.ShowMessage(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<MessageBoxButton>(), Arg.Any<MessageBoxImage>())
                .Returns(_ => throw new InvalidOperationException("dialog failed"));

            var keepsRunning = _handler.HandleDispatcherException(new InvalidOperationException("boom"));

            Assert.False(keepsRunning);
        }
    }
}
```

- [ ] **Step 7: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

- [ ] **Step 8: Commit**

```bash
git add TaskManager/Services/ErrorHandling TaskManager/App.xaml.cs TaskManager.Shared/Resources/Languages/Strings.resx TaskManager.Shared/Resources/Languages/Strings.Designer.cs TaskManager.Tests/ErrorHandling/UiErrorHandlerTests.cs
git commit -m "feat(app): add UI error handler with guarded command helpers"
```

---

### Task 3: Global Handler Wiring in App

**Files:**
- Modify: `TaskManager/App.xaml.cs` (OnStartup ordering + registration method)

**Interfaces:**
- Consumes: `IErrorHandler.HandleDispatcherException/LogFatal/LogUnobserved` (exact signatures from Task 2).
- Produces: runtime behavior only — no new types.

- [ ] **Step 1: Register handlers early in OnStartup**

Modify `TaskManager/App.xaml.cs`. Handlers must be active before any code that can fail (settings resolution happens when `LaunchGUI` resolves `MainWindow`). Replace `OnStartup` and add the registration method:

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    var serviceCollection = new ServiceCollection();
    ConfigureServices(serviceCollection);

    _serviceProvider = serviceCollection.BuildServiceProvider();

    RegisterGlobalExceptionHandlers();

    SetLanguage();

    base.OnStartup(e);

    LaunchGUI();
}

private void RegisterGlobalExceptionHandlers()
{
    var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();

    DispatcherUnhandledException += (_, e) =>
        e.Handled = errorHandler.HandleDispatcherException(e.Exception);

    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        if (e.ExceptionObject is Exception exception)
        {
            errorHandler.LogFatal(exception);
        }
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        errorHandler.LogUnobserved(e.Exception);
        e.SetObserved();
    };
}
```

- [ ] **Step 2: Manual smoke check**

Run the app, let it start normally, then stop it — no regressions. (Fault-path smoke testing of global handlers is manual by design; spec §5.)

- [ ] **Step 3: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add TaskManager/App.xaml.cs
git commit -m "feat(app): register global exception handlers as last-resort tier"
```

---

### Task 4: Settings Fallback Crash Fix

The bug: `LoadDefaultSettings` executes `Convert.ToInt32(nameof(Settings.Default.RefreshFrequency))` — converting the literal string `"RefreshFrequency"` throws `FormatException` unconditionally. If `LoadSettings()` ever fails, the recovery path crashes inside a DI singleton constructor at startup.

**Files:**
- Modify: `TaskManager/Services/SettingsService.cs`
- Test (create): `TaskManager.Tests/Services/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `ILogger<SettingsService>` (Task 1 wiring), `IMessageService`.
- Produces: `SettingsService` constructor becomes `(IMessageService messageService, ILogger<SettingsService> logger)`; `LoadSettings()` becomes `protected virtual` (test seam). Public surface otherwise unchanged.

- [ ] **Step 1: Harden SettingsService**

Rewrite the affected members of `TaskManager/Services/SettingsService.cs`:

Add field + change constructor (keep `using Microsoft.Extensions.Logging;` added at top):

```csharp
private readonly IMessageService _messageService;
private readonly ILogger<SettingsService> _logger;

public SettingsService(IMessageService messageService, ILogger<SettingsService> logger)
{
    _messageService = messageService;
    _logger = logger;

    ProcessPropertyValues();
}
```

Replace `ProcessPropertyValues`, make `LoadSettings` virtual, fix `LoadDefaultSettings`, add `TryLoadDefaultSettings`:

```csharp
private void ProcessPropertyValues()
{
    try
    {
        LoadSettings();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Loading personalized settings failed; falling back to defaults");
        _messageService.ShowMessage(Strings.LoadingSettingsFailed, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
        TryLoadDefaultSettings();
    }
}

protected virtual void LoadSettings()
{
    Language = Settings.Default.LanguageVersion;
    RefreshFrequency = (RefreshFrequencyType)int.Parse(Settings.Default.RefreshFrequency);
    DateTimeFormat = Settings.Default.DateTimeFormat;
}

private void LoadDefaultSettings()
{
    Language = (string?)GetDefaultSettingValue(nameof(Settings.Default.LanguageVersion)) ?? string.Empty;
    RefreshFrequency = (RefreshFrequencyType)int.Parse(
        (string?)GetDefaultSettingValue(nameof(Settings.Default.RefreshFrequency)) ?? ((int)RefreshFrequencyType.Low).ToString());
    DateTimeFormat = (string?)GetDefaultSettingValue(nameof(Settings.Default.DateTimeFormat)) ?? string.Empty;
}

private void TryLoadDefaultSettings()
{
    try
    {
        LoadDefaultSettings();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Loading default settings failed; keeping construction-time values");
    }
}
```

(The old commented-out `Debug.WriteLine(ex)` call is removed — logging replaces it.)

- [ ] **Step 2: Add regression test**

Create `TaskManager.Tests/Services/SettingsServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Properties;
using TaskManager.Services;

namespace TaskManager.Tests
{
    /// <summary>
    /// Regression contract: corrupt personalized settings degrade to defaults.
    /// Neither the initial load nor the fallback may escape the constructor.
    /// </summary>
    public class SettingsServiceTests
    {
        private sealed class CorruptSettingsService : SettingsService
        {
            public CorruptSettingsService(IMessageService messageService)
                : base(messageService, NullLogger<SettingsService>.Instance)
            {
            }

            protected override void LoadSettings() =>
                throw new InvalidOperationException("simulated corrupt settings store");
        }

        [Fact]
        public void Constructor_WithCorruptSettings_FallsBackToDefaultsWithoutThrowing()
        {
            var messageService = Substitute.For<IMessageService>();
            var expectedLanguage = (string?)Settings.Default.Properties[nameof(Settings.Default.LanguageVersion)].DefaultValue;
            var expectedFrequencyText = (string?)Settings.Default.Properties[nameof(Settings.Default.RefreshFrequency)].DefaultValue;
            var expectedRefreshFrequency = (RefreshFrequencyType)int.Parse(expectedFrequencyText!);

            var service = new CorruptSettingsService(messageService);

            messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
            Assert.Equal(expectedLanguage, service.Language);
            Assert.Equal(expectedRefreshFrequency, service.RefreshFrequency);
        }
    }
}
```

- [ ] **Step 3: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add TaskManager/Services/SettingsService.cs TaskManager.Tests/Services/SettingsServiceTests.cs
git commit -m "fix(settings): make corrupt-settings recovery exception-safe"
```

---

### Task 5: ProcessManager Hardening (summaries, polling guard, enumeration logging)

**Files:**
- Create: `TaskManager.Domain/Models/ProcessOpSummary.cs`
- Modify: `TaskManager.Domain/Services/ProcessManager.cs`
- Modify: `TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs`

**Interfaces:**
- Consumes: `ILogger<ProcessManager>` (Task 1).
- Produces (consumed by Task 7):
  - `TaskManager.Domain.Models.ProcessOpFailureReason` enum: `ProcessExited, AccessDenied, Unknown`
  - `TaskManager.Domain.Models.ProcessOpFailure(int Pid, ProcessOpFailureReason Reason)` record
  - `TaskManager.Domain.Models.ProcessOpSummary` record: `IReadOnlyList<int> SucceededPids`, `IReadOnlyList<ProcessOpFailure> Failures`, `bool HasFailures`, static `Empty`
  - Changed signatures: `ProcessOpSummary TerminateProcesses(IEnumerable<int> selectedProcesses)`, `ProcessOpSummary SetPriority(IEnumerable<int> selectedProcesses, ProcessPriorityClass priority)`, ctor `ProcessManager(IDispatcherService, IAppSettings, TimerManager, ILogger<ProcessManager>)`, `internal Task SafePollingRefreshAsync()`

- [ ] **Step 1: Create result types**

Create `TaskManager.Domain/Models/ProcessOpSummary.cs`:

```csharp
namespace TaskManager.Domain.Models
{
    public enum ProcessOpFailureReason
    {
        ProcessExited,
        AccessDenied,
        Unknown
    }

    public sealed record ProcessOpFailure(int Pid, ProcessOpFailureReason Reason);

    /// <summary>
    /// Per-PID outcome of a batch process operation. Expected OS failures are data, not exceptions.
    /// </summary>
    public sealed record ProcessOpSummary
    {
        public static readonly ProcessOpSummary Empty = new();

        public IReadOnlyList<int> SucceededPids { get; init; } = [];
        public IReadOnlyList<ProcessOpFailure> Failures { get; init; } = [];

        public bool HasFailures => Failures.Count > 0;
    }
}
```

- [ ] **Step 2: Harden ProcessManager**

Modify `TaskManager.Domain/Services/ProcessManager.cs`. Add usings:

```csharp
using Microsoft.Extensions.Logging;
using System.ComponentModel;
```

Add field and change constructor:

```csharp
private readonly ILogger<ProcessManager> _logger;

public ProcessManager(IDispatcherService dispatcher, IAppSettings settings, TimerManager timerManager,
    ILogger<ProcessManager> logger)
{
    _dispatcher = dispatcher;
    _settings = settings;
    _timer = timerManager;
    _logger = logger;

    Processes.CollectionChanged += Processes_CollectionChanged;

    _timer.Elapsed += OnProcessPolling;
}
```

Replace `OnProcessPolling` and add `SafePollingRefreshAsync` (async void can no longer leak exceptions):

```csharp
private async void OnProcessPolling(object? sender, System.Timers.ElapsedEventArgs e)
{
    await SafePollingRefreshAsync();
}

internal async Task SafePollingRefreshAsync()
{
    try
    {
        await PerformRefresh(isUserInitiated: false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Polling refresh failed");
    }
}
```

Replace `TerminateProcesses` and `SetPriority`, add shared helpers:

```csharp
private const int ErrorAccessDenied = 5;

public ProcessOpSummary TerminateProcesses(IEnumerable<int> selectedProcesses)
{
    return ExecutePerPid(selectedProcesses, pid =>
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        process.Kill();
        _logger.LogDebug("Process {Pid} was terminated", pid);
    });
}

public ProcessOpSummary SetPriority(IEnumerable<int> selectedProcesses, System.Diagnostics.ProcessPriorityClass priority)
{
    var summary = ExecutePerPid(selectedProcesses, pid =>
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        process.PriorityClass = priority;
    });

    foreach (var pid in summary.SucceededPids)
    {
        var storedItem = Processes.FirstOrDefault(p => p.Process.Pid == pid);
        storedItem?.Process.Priority = PriorityTypeHelper.GetBasePriority(priority);
    }

    return summary;
}

/// <remarks>
/// Expected OS-level rejections are recorded per PID and never abort the batch;
/// they are data (<see cref="ProcessOpSummary"/>), not exceptions crossing the layer boundary.
/// </remarks>
private ProcessOpSummary ExecutePerPid(IEnumerable<int> selectedPids, Action<int> operation)
{
    var succeeded = new List<int>();
    var failures = new List<ProcessOpFailure>();

    foreach (var pid in selectedPids)
    {
        try
        {
            operation(pid);
            succeeded.Add(pid);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            var reason = ClassifyFailure(ex);
            _logger.LogWarning(ex, "Process operation failed for PID {Pid} ({Reason})", pid, reason);
            failures.Add(new ProcessOpFailure(pid, reason));
        }
    }

    return new ProcessOpSummary { SucceededPids = succeeded, Failures = failures };
}

private static ProcessOpFailureReason ClassifyFailure(Exception ex) => ex switch
{
    Win32Exception { NativeErrorCode: ErrorAccessDenied } => ProcessOpFailureReason.AccessDenied,
    ArgumentException => ProcessOpFailureReason.ProcessExited,
    InvalidOperationException => ProcessOpFailureReason.ProcessExited,
    _ => ProcessOpFailureReason.Unknown
};
```

Behavior note (intentional): previously `SetPriority` updated the displayed priority even when the OS update had failed for a stale PID; now the model is updated only for succeeded PIDs. The existing stale-PID test still passes because it asserts the surviving process.

Change `GetProcesses()` from `private static` to instance (it needs `_logger`), and replace its swallow:

```csharp
private IEnumerable<System.Diagnostics.Process> GetProcesses()
```

```csharp
catch (Exception ex)
{
    _logger.LogDebug(ex, "Skipped inaccessible process during enumeration");
    continue;
}
```

(Remove the old commented-out `Debug.WriteLine` line. Keep the existing remarks doc comment.)

- [ ] **Step 3: Update and extend ProcessManager tests**

Modify `TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs`:

Add usings:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Domain.Models;
using WinProcessStartInfo = System.Diagnostics.ProcessStartInfo;
```

Update constructor (new third-party dependency order: logger last):

```csharp
_manager = new ProcessManager(
    Substitute.For<IDispatcherService>(),
    settings,
    new TimerManager(settings),
    NullLogger<ProcessManager>.Instance);
```

Add summary-contract tests inside the class:

```csharp
[Fact]
public void SetPriority_StalePid_IsReportedInSummary()
{
    int stalePid = GetUnusedPid();

    var summary = _manager.SetPriority(new[] { stalePid }, ProcessPriorityClass.Normal);

    Assert.Empty(summary.SucceededPids);
    var failure = Assert.Single(summary.Failures);
    Assert.Equal(stalePid, failure.Pid);
    Assert.Equal(ProcessOpFailureReason.ProcessExited, failure.Reason);
}

[Fact]
public void SetPriority_MixedBatch_ReportsBothOutcomesAndSurvives()
{
    int stalePid = GetUnusedPid();

    var summary = _manager.SetPriority(new[] { stalePid, _self.Id }, ProcessPriorityClass.AboveNormal);

    Assert.Equal(new[] { _self.Id }, summary.SucceededPids);
    Assert.Single(summary.Failures);
    _self.Refresh();
    Assert.Equal(ProcessPriorityClass.AboveNormal, _self.PriorityClass);
}

[Fact]
public void TerminateProcesses_KillsTarget_AndReportsStalePidWithoutAborting()
{
    using var victim = WinProcess.Start(new WinProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = "/c ping -n 30 127.0.0.1 > nul",
        UseShellExecute = false,
        CreateNoWindow = true
    });
    int stalePid = GetUnusedPid();

    try
    {
        var summary = _manager.TerminateProcesses(new[] { victim.Id, stalePid });

        Assert.Equal(new[] { victim.Id }, summary.SucceededPids);
        var failure = Assert.Single(summary.Failures);
        Assert.Equal(stalePid, failure.Pid);
        Assert.True(victim.WaitForExit(5_000));
        Assert.True(victim.HasExited);
    }
    finally
    {
        if (!victim.HasExited)
        {
            try { victim.Kill(); } catch { /* best-effort cleanup */ }
        }
    }
}

[Fact]
public async Task SafePollingRefreshAsync_SwallowsAndLogsUnexpectedFailures()
{
    var throwingDispatcher = Substitute.For<IDispatcherService>();
    throwingDispatcher
        .When(d => d.Invoke(Arg.Do<Action>(_ => throw new InvalidOperationException("dispatcher died"))))
        .Do(_ => { });
    var failingManager = new ProcessManager(
        throwingDispatcher,
        Substitute.For<IAppSettings>(),
        new TimerManager(Substitute.For<IAppSettings>()),
        NullLogger<ProcessManager>.Instance);
    failingManager.Processes.Add(new ProcessItem(new Process { Name = "ghost", Pid = GetUnusedPid(), Path = string.Empty }));

    await failingManager.SafePollingRefreshAsync();
}
```

(The last test passes iff no exception escapes `SafePollingRefreshAsync` — the dispatcher mock makes the removal loop throw; the timer settings substitute must also satisfy `TimerManager`'s mapping lookup, so give it `RefreshFrequency` like the main fixture does — reuse the fixture pattern: create it through the same helper as `_manager` but with the throwing dispatcher. Simplest correct form: build `failingManager` with the same `settings` instance as `_manager` — hoist `settings` into a field.)

Concretely, refactor the fixture so `settings` is a field:

```csharp
private readonly IAppSettings _settings;

public ProcessManagerTests()
{
    _settings = Substitute.For<IAppSettings>();
    _settings.RefreshFrequency.Returns(RefreshFrequencyType.Low); // timer is never started

    _manager = new ProcessManager(
        Substitute.For<IDispatcherService>(),
        _settings,
        new TimerManager(_settings),
        NullLogger<ProcessManager>.Instance);
    _originalPriority = _self.PriorityClass;
}
```

and construct `failingManager` with `new TimerManager(_settings)` too.

- [ ] **Step 4: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

Expected compile break outside tests: none yet — `TerminateProcesses`/`SetPriority` callers are updated in Task 7, but their return values were ignored (`void`→summary is source-compatible for statement invocation).

- [ ] **Step 5: Commit**

```bash
git add TaskManager.Domain/Models/ProcessOpSummary.cs TaskManager.Domain/Services/ProcessManager.cs "TaskManager.Tests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs"
git commit -m "refactor(domain): report batch process outcomes as data instead of exceptions"
```

---

### Task 6: ExportResult and Exporter Failure Mapping

**Files:**
- Create: `TaskManager.Domain/Models/ExportResult.cs`
- Modify: `TaskManager.Domain/Services/Data Export/BaseDataExporter.cs`
- Modify: `TaskManager/Services/Factories/DataExporterFactory.cs`
- Test (create): `TaskManager.Tests/DataExport/BaseDataExporterTests.cs`

**Interfaces:**
- Consumes: `ILogger<BaseDataExporter>` (Task 1), `IAppSettings.DateTimeFormat`.
- Produces (consumed by Task 7):
  - `TaskManager.Domain.Models.ExportFailureReason` enum: `IoError, AccessDenied, InvalidPath`
  - `TaskManager.Domain.Models.ExportResult` record: `string? FilePath`, `ExportFailureReason? FailureReason`, `bool IsSuccess`, statics `Success(string)` / `Fail(ExportFailureReason)`
  - Changed signature: `ExportResult Export<T>(string dirPath, IEnumerable<T> records) where T : IExportable`
  - Changed exporter ctors: `(IAppSettings settings, ILogger<BaseDataExporter> logger)`

- [ ] **Step 1: Create ExportResult**

Create `TaskManager.Domain/Models/ExportResult.cs`:

```csharp
namespace TaskManager.Domain.Models
{
    public enum ExportFailureReason
    {
        IoError,
        AccessDenied,
        InvalidPath
    }

    /// <summary>
    /// Outcome of a data export. Expected IO failures are data, not exceptions.
    /// </summary>
    public sealed record ExportResult
    {
        public string? FilePath { get; private init; }
        public ExportFailureReason? FailureReason { get; private init; }
        public bool IsSuccess => FailureReason is null;

        public static ExportResult Success(string filePath) => new() { FilePath = filePath };

        public static ExportResult Fail(ExportFailureReason reason) => new() { FailureReason = reason };
    }
}
```

- [ ] **Step 2: Wrap export in BaseDataExporter**

Modify `TaskManager.Domain/Services/Data Export/BaseDataExporter.cs` — add `using Microsoft.Extensions.Logging;`, add the logger field/ctor param, change `Export<T>` return type, add classification:

```csharp
private readonly IAppSettings _settings;
private readonly ILogger<BaseDataExporter> _logger;

public BaseDataExporter(IAppSettings settings, ILogger<BaseDataExporter> logger)
{
    _settings = settings;
    _logger = logger;
}

public ExportResult Export<T>(string dirPath, IEnumerable<T> records) where T : IExportable
{
    try
    {
        IList<string> strings = GetStrings(records).ToList();

        string fullFileName = dirPath + GenerateFileName(Extension);
        PerformExport<T>(fullFileName, strings);
        return ExportResult.Success(fullFileName);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        var reason = ClassifyFailure(ex);
        _logger.LogWarning(ex, "Export as {Extension} failed ({Reason})", Extension, reason);
        return ExportResult.Fail(reason);
    }
}

private static ExportFailureReason ClassifyFailure(Exception ex) => ex switch
{
    ArgumentException or NotSupportedException => ExportFailureReason.InvalidPath,
    UnauthorizedAccessException => ExportFailureReason.AccessDenied,
    _ => ExportFailureReason.IoError
};
```

Concrete exporters (`CsvExporter`, `TxtExporter`, `JsonExporter`, `XmlExporter`, `ExcelExporter`) each change only their constructor to forward the logger, e.g.:

```csharp
public CsvExporter(IAppSettings settings, ILogger<BaseDataExporter> logger)
    : base(settings, logger)
{
}
```

(add `using Microsoft.Extensions.Logging;` where missing)

- [ ] **Step 3: Update DataExporterFactory**

Modify `TaskManager/Services/Factories/DataExporterFactory.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Services.Data_Export;
using TaskManager.Utility.Utility;

// inside CreateDataExporter:
var settings = _serviceProvider.GetRequiredService<IAppSettings>();
var exporterLogger = _serviceProvider.GetRequiredService<ILogger<BaseDataExporter>>();

return dataType switch
{
    DataType.Csv => new CsvExporter(settings, exporterLogger),
    DataType.Txt => new TxtExporter(settings, exporterLogger),
    DataType.Xlsx => new ExcelExporter(settings, exporterLogger),
    DataType.Json => new JsonExporter(settings, exporterLogger),
    DataType.Xml => new XmlExporter(settings, exporterLogger),
    _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, null),
};
```

- [ ] **Step 4: Add tests**

Create `TaskManager.Tests/DataExport/BaseDataExporterTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Data_Export;

namespace TaskManager.Tests
{
    public class BaseDataExporterTests : IDisposable
    {
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-export-tests-{Guid.NewGuid():N}");

        private static IAppSettings CreateSettings()
        {
            var settings = Substitute.For<IAppSettings>();
            settings.DateTimeFormat.Returns("yyyyMMdd_HHmmss");
            return settings;
        }

        [Fact]
        public void Export_Success_ReturnsFilePath()
        {
            Directory.CreateDirectory(_tempDirectory);
            var exporter = new TxtExporter(CreateSettings(), NullLogger<BaseDataExporter>.Instance);

            var result = exporter.Export<DummyRecord>(_tempDirectory, []);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.FilePath);
            Assert.StartsWith(Path.Combine(_tempDirectory, @"\record-"), result.FilePath);
            Assert.EndsWith(".txt", result.FilePath);
        }

        [Theory]
        [InlineData(typeof(IOException), ExportFailureReason.IoError)]
        [InlineData(typeof(UnauthorizedAccessException), ExportFailureReason.AccessDenied)]
        [InlineData(typeof(ArgumentException), ExportFailureReason.InvalidPath)]
        [InlineData(typeof(NotSupportedException), ExportFailureReason.InvalidPath)]
        public void Export_MapsExpectedIoFailuresToOutcomeData(Type exceptionType, ExportFailureReason expectedReason)
        {
            var failure = (Exception)Activator.CreateInstance(exceptionType, "simulated io failure")!;
            var exporter = new ThrowingExporter(CreateSettings(), failure);

            var result = exporter.Export<DummyRecord>("C:\\dir", []);

            Assert.False(result.IsSuccess);
            Assert.Equal(expectedReason, result.FailureReason);
        }

        [Fact]
        public void Export_LetsUnexpectedExceptionsPropagate()
        {
            var exporter = new ThrowingExporter(CreateSettings(), new InvalidOperationException("a bug"));

            Assert.Throws<InvalidOperationException>(
                () => exporter.Export<DummyRecord>("C:\\dir", []));
        }

        /// <summary>Satisfies the IExportable generic constraint; never instantiated (empty record lists).</summary>
        private sealed class DummyRecord : IExportable
        {
            public string ToDelimitedString(char separator) => string.Empty;
        }

        private sealed class ThrowingExporter(IAppSettings settings, Exception failureToThrow)
            : BaseDataExporter(settings, NullLogger<BaseDataExporter>.Instance)
        {
            protected override string Extension => "txt";

            protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
                => throw failureToThrow;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
```

(Note: `ThrowingExporter` uses a C# primary constructor for brevity here; if the repo style rejects it, expand to a classic constructor storing the exception.)

- [ ] **Step 5: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add TaskManager.Domain/Models/ExportResult.cs "TaskManager.Domain/Services/Data Export/BaseDataExporter.cs" TaskManager.Domain/Services/"Data Export" TaskManager/Services/Factories/DataExporterFactory.cs TaskManager.Tests/DataExport/BaseDataExporterTests.cs
git commit -m "refactor(domain): exports return outcome data instead of throwing through layers"
```

---

### Task 7: ViewModel Adoptions, Startup Guard, Dead Code Removal

**Files:**
- Modify: `TaskManager/ViewModels/MainWindowViewModel.cs`
- Modify: `TaskManager/App.xaml.cs` (remove dead `App_Close` event plumbing)
- Modify: `TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- Modify: `TaskManager/Services/Factories/SetPriorityVVmFactory.cs`
- Modify: `TaskManager/ViewModels/SettingsWindowViewModel.cs`
- Modify: `TaskManager/ViewModels/DataExportWindowViewModel.cs`
- Modify: `TaskManager/Services/Factories/DataExportViewModelFactory.cs`
- Modify: `TaskManager/TaskManager.csproj` (InternalsVisibleTo for NSubstitute dynamic proxy)
- Modify: `TaskManager.Shared/Resources/Languages/Strings.resx` + `Strings.Designer.cs`
- Test (create): `TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2–6 (exact signatures above).
- Produces: changed ViewModels ctors — `MainWindowViewModel(IServiceProvider, IMessageService, IDispatcherService, ProcessManager, IErrorHandler)`, `SetPriorityWindowViewModel(IMessageService, ProcessManager, IEnumerable<int>, IErrorHandler)`, `SettingsWindowViewModel(ISettingsService, IErrorHandler)`, `DataExportWindowViewModel(IServiceProvider, IAppSettings, IMessageService, IErrorHandler, IEnumerable<Process>)`; `DataExportWindowViewModel.internal bool TryExport(ExportationType, DataType)` (test seam); `DataExporterFactory.virtual CreateDataExporter(DataType)`.

- [ ] **Step 1: Make DataExporterFactory mockable and allow dynamic proxies**

In `TaskManager/Services/Factories/DataExporterFactory.cs` change the method declaration:

```csharp
public virtual BaseDataExporter CreateDataExporter(DataType dataType)
```

In `TaskManager/TaskManager.csproj`, extend the InternalsVisibleTo item group (line ~39):

```xml
<InternalsVisibleTo Include="TaskManager.Tests"/>
<InternalsVisibleTo Include="DynamicProxyGenAssembly2"/>
```

- [ ] **Step 2: Add remaining localized strings**

Append to `Strings.resx` before `</root>`:

```xml
  <data name="OpsCompletedWithFailuresFormat" xml:space="preserve">
    <value>{0} of {1} operations succeeded.</value>
  </data>
  <data name="ExportFailedFormat" xml:space="preserve">
    <value>Exporting data to '{0}' failed.</value>
  </data>
  <data name="ExportFailedIo" xml:space="preserve">
    <value>The file could not be written (I/O error).</value>
  </data>
  <data name="ExportFailedAccessDenied" xml:space="preserve">
    <value>Access to the target folder was denied.</value>
  </data>
  <data name="ExportFailedInvalidPath" xml:space="preserve">
    <value>The target path is invalid.</value>
  </data>
```

Append matching accessor properties to `Strings.Designer.cs` (same pattern as Step 1 of Task 2): `OpsCompletedWithFailuresFormat`, `ExportFailedFormat`, `ExportFailedIo`, `ExportFailedAccessDenied`, `ExportFailedInvalidPath`.

- [ ] **Step 3: MainWindowViewModel**

Changes in `TaskManager/ViewModels/MainWindowViewModel.cs`:

Add using: `using TaskManager.Services.ErrorHandling;`

Field + constructor (new parameter, guarded startup load, removed dead event subscription):

```csharp
private readonly IErrorHandler _errorHandler;

public MainWindowViewModel(IServiceProvider serviceProvider,
    IMessageService messageService,
    IDispatcherService dispatcherService,
    ProcessManager processManager,
    IErrorHandler errorHandler)
{
    _serviceProvider = serviceProvider;
    _messageService = messageService;
    _dispatcherService = dispatcherService;
    _processManager = processManager;
    _errorHandler = errorHandler;

    ExportCommand = new RelayCommand(Export);
    TerminateCommand = new RelayCommand(TerminateProcesses);
    SetPriorityCommand = new RelayCommand(SetPriority);
    OpenSettingsCommand = new RelayCommand(OpenSettings);
    RefreshCommand = new AsyncRelayCommand(() => _processManager.PerformRefresh(isUserInitiated: true));

    // load running processes synchronously; a startup failure must not abort the app
    _errorHandler.Guard(() => _processManager.LoadProcesses().GetAwaiter().GetResult(),
        "loading initial process list");
    _processManager.StartPollingProcesses();

    // binding synchronizers initialization
    _processManager.PropertyChanged += ProcessManager_PropertyChanged;

    // late add events
    _processManager.Processes.CollectionChanged += _processManager.Processes_CollectionChanged;
}
```

(Delete the line `App.App_Close += App_Close;` and the empty `private async void App_Close(object sender)` method at the bottom.)

Terminate command consumes the summary:

```csharp
private void TerminateProcesses()
{
    if (!ValidatePreconditions(Preconditions.SelectedAnyProcess))
    {
        return;
    }
    if (_messageService.ShowMessage(Strings.AskingForConfirmation, Strings.Confirm, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
        == MessageBoxResult.Cancel)
    {
        return;
    }

    _errorHandler.Guard(() =>
    {
        var summary = _processManager.TerminateProcesses(GetSelectedProcesses().Select(x => Convert.ToInt32(x.Process.Pid)));
        ReportPartialFailures(summary);
    });
}

private void ReportPartialFailures(ProcessOpSummary summary)
{
    if (!summary.HasFailures)
    {
        return;
    }

    var total = summary.SucceededPids.Count + summary.Failures.Count;
    _messageService.ShowMessage(
        string.Format(Strings.OpsCompletedWithFailuresFormat, summary.SucceededPids.Count, total),
        Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

Add using for the summary type: `using TaskManager.Domain.Models;` (already present in this file).

OpenSettings passes the handler through:

```csharp
settingsWindow.DataContext = new SettingsWindowViewModel(settingsService, _errorHandler);
```

- [ ] **Step 4: Remove App.Close dead plumbing**

In `TaskManager/App.xaml.cs` delete:
- `public delegate void App_Close(object sender);` (line 16)
- `public static event App_Close? App_Close;` (line 25)
- the `App_Close?.Invoke(this);` line inside `Application_Exit` (the now-empty `Application_Exit` method itself stays because `App.xaml` references it via `Exit="Application_Exit"`).

- [ ] **Step 5: SetPriorityWindowViewModel**

```csharp
private readonly IErrorHandler _errorHandler;

public SetPriorityWindowViewModel(IMessageService messageService, ProcessManager processManager,
    IEnumerable<int> processes, IErrorHandler errorHandler)
{
    _messageService = messageService;
    _processManager = processManager;
    _processIds = processes;
    _errorHandler = errorHandler;
    OnConfirmCommand = new RelayCommand(OnConfirm);
}

private void OnConfirm()
{
    _errorHandler.Guard(() =>
    {
        if (Priority == null)
        {
            _messageService.ShowMessage(Strings.Select, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var summary = _processManager.SetPriority(_processIds, (ProcessPriorityClass)Priority);
        ReportPartialFailures(summary);

        var window = GetAssociatedWindow<SetPriorityWindow>();
        window?.Close();
    }, "applying the selected priority");
}

private void ReportPartialFailures(ProcessOpSummary summary)
{
    if (!summary.HasFailures)
    {
        return;
    }

    var total = summary.SucceededPids.Count + summary.Failures.Count;
    _messageService.ShowMessage(
        string.Format(Strings.OpsCompletedWithFailuresFormat, summary.SucceededPids.Count, total),
        Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

Add usings: `using TaskManager.Domain.Models;`, `using TaskManager.Services.ErrorHandling;`.

Update `TaskManager/Services/Factories/SetPriorityVVmFactory.cs`:

```csharp
var messageService = _serviceProvider.GetRequiredService<IMessageService>();
var processManager = _serviceProvider.GetRequiredService<ProcessManager>();
var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();
var setPriorityWindowVM = new SetPriorityWindowViewModel(messageService, processManager, processes, errorHandler);
```

- [ ] **Step 6: SettingsWindowViewModel**

```csharp
private readonly ISettingsService _settingsService;
private readonly IErrorHandler _errorHandler;

public SettingsWindowViewModel(ISettingsService settingsService, IErrorHandler errorHandler)
{
    _settingsService = settingsService;
    _errorHandler = errorHandler;

    EditableSettings = settingsService.ToEditables();

    SaveSettingsCommand = new RelayCommand(SaveSettings);
    RestoreDefaultsCommand = new RelayCommand(_settingsService.RestoreDefaults);
}

public void SaveSettings()
{
    _errorHandler.Guard(() => _settingsService.SaveSettings(EditableSettings), "saving settings");
}
```

Add using: `using TaskManager.Services.ErrorHandling;`.

- [ ] **Step 7: DataExportWindowViewModel**

Restructure so core logic is window-independent and testable (`internal bool TryExport(...)`); the window closes only on success:

```csharp
private readonly IServiceProvider _serviceProvider;
private readonly IAppSettings _settings;
private readonly IMessageService _messageService;
private readonly IErrorHandler _errorHandler;

public DataExportWindowViewModel(IServiceProvider serviceProvider,
    IAppSettings settings,
    IMessageService messageService,
    IErrorHandler errorHandler,
    IEnumerable<Process> processes)
{
    _serviceProvider = serviceProvider;
    _settings = settings;
    _messageService = messageService;
    _errorHandler = errorHandler;
    this.processes = processes;

    SelectFolderCommand = new RelayCommand(_folderSelector.SelectFolder);
    OnConfirmClick = new RelayCommand(OnConfirm);

    _folderSelector.PropertyChanged += FolderSelector_PropertyChanged;
}

private void OnConfirm()
{
    _errorHandler.Guard(() =>
    {
        if (Exportation is not ExportationType exportation || DataType is not DataType dataType)
        {
            _messageService.ShowMessage("You need to select options", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!TryExport(exportation, dataType))
        {
            return; // failure already reported; keep the window open for a corrected attempt
        }

        GetAssociatedWindow<DataExportWindow>().DialogResult = true;
    }, "exporting process data");
}

internal bool TryExport(ExportationType exportation, DataType dataType)
{
    var exporter = _serviceProvider.GetRequiredService<DataExporterFactory>().CreateDataExporter(dataType);

    switch (exportation)
    {
        case ExportationType.Processes:
            var result = exporter.Export(dirPath, processes);
            if (result.IsSuccess)
            {
                return true;
            }

            _messageService.ShowMessage(
                string.Format(Strings.ExportFailedFormat, dirPath) + " " + DescribeFailure(result.FailureReason!.Value),
                Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
    }

    return false;
}

private static string DescribeFailure(ExportFailureReason reason) => reason switch
{
    ExportFailureReason.AccessDenied => Strings.ExportFailedAccessDenied,
    ExportFailureReason.InvalidPath => Strings.ExportFailedInvalidPath,
    _ => Strings.ExportFailedIo
};
```

Add usings: `using TaskManager.Services.ErrorHandling;` (others already present).

Update `TaskManager/Services/Factories/DataExportViewModelFactory.cs`:

```csharp
public DataExportWindowViewModel Create(IEnumerable<Process> data)
{
    var settings = _serviceProvider.GetRequiredService<IAppSettings>();
    var messageService = _serviceProvider.GetRequiredService<IMessageService>();
    var errorHandler = _serviceProvider.GetRequiredService<IErrorHandler>();

    return new DataExportWindowViewModel(_serviceProvider, settings, messageService, errorHandler, data);
}
```

- [ ] **Step 8: Add export VM tests**

Create `TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs`:

Two implementation details drive the test setup below:
- The exporter receives the VM's `DirPath` property value; set it in the constructor so real writes land in a temp directory.
- `DataType` collides between the VM property name and the enum type — alias the enum as `DataTypeEnum` proactively.

Create `TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs`:

```csharp
_viewModel.DirPath = _tempDirectory;
```

and drop `_tempDirectory` from the exporter construction entirely. Also note `DataType.Txt` collides with the property named `DataType` on the VM — inside the test class there is no conflict since we reference the enum type directly; if the compiler resolves ambiguity, alias it: `using DataTypeEnum = TaskManager.Utility.DataType;` and use `DataTypeEnum.Txt`. Prefer the alias proactively.

Final test-file shape:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Data_Export;
using TaskManager.Services.ErrorHandling;
using TaskManager.Services.Factories;
using TaskManager.Utility.Utility;
using TaskManager.ViewModels;
using DataTypeEnum = TaskManager.Utility.DataType;

namespace TaskManager.Tests
{
    public class DataExportWindowViewModelTests : IDisposable
    {
        private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly DataExporterFactory _exporterFactory =
            Substitute.For<DataExporterFactory>(Substitute.For<IServiceProvider>());
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-exportvm-tests-{Guid.NewGuid():N}");
        private readonly DataExportWindowViewModel _viewModel;

        public DataExportWindowViewModelTests()
        {
            Directory.CreateDirectory(_tempDirectory);
            _serviceProvider.GetRequiredService<DataExporterFactory>().Returns(_exporterFactory);
            _viewModel = new DataExportWindowViewModel(
                _serviceProvider,
                Substitute.For<IAppSettings>(),
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, _messageService),
                []);
            _viewModel.DirPath = _tempDirectory;
        }

        [Fact]
        public void TryExport_Success_WritesFileAndReturnsTrue()
        {
            _exporterFactory
                .CreateDataExporter(DataTypeEnum.Txt)
                .Returns(new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance));

            var success = _viewModel.TryExport(ExportationType.Processes, DataTypeEnum.Txt);

            Assert.True(success);
            Assert.NotEmpty(Directory.GetFiles(_tempDirectory, "record-*"));
        }

        [Fact]
        public void TryExport_Failure_ShowsSingleMessageAndReturnsFalse()
        {
            _exporterFactory
                .CreateDataExporter(DataTypeEnum.Txt)
                .Returns(new ThrowingExporter(NewSettings()));

            var success = _viewModel.TryExport(ExportationType.Processes, DataTypeEnum.Txt);

            Assert.False(success);
            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void TryExport_UnexpectedExporterCrash_IsGuardedAndDoesNotThrow()
        {
            _exporterFactory
                .When(f => f.CreateDataExporter(DataTypeEnum.Txt))
                .Do(_ => throw new InvalidOperationException("factory exploded"));

            var success = _viewModel.TryExport(ExportationType.Processes, DataTypeEnum.Txt);

            Assert.False(success);
        }

        private static IAppSettings NewSettings()
        {
            var settings = Substitute.For<IAppSettings>();
            settings.DateTimeFormat.Returns("yyyyMMdd_HHmmss");
            return settings;
        }

        private sealed class ThrowingExporter(IAppSettings settings) : TxtExporter(settings, NullLogger<BaseDataExporter>.Instance)
        {
            protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
                => throw new IOException("target locked");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
```

Notes for the implementer:
- `PerformExport` is `protected abstract` on `BaseDataExporter` — overriding it in `ThrowingExporter` is legal.
- `TryExport` is `internal`; `InternalsVisibleTo` (already present) makes it visible to the test project.
- `DirPath` setter propagates to `_folderSelector.DirPath` — harmless in tests.
- The third test relies on Task 7 Step 1 (`virtual CreateDataExporter`): `.When(...).Do(throw)` on a NSubstitute partial makes the call throw.

- [ ] **Step 9: Validate**

```bash
dotnet build TaskManager.sln
dotnet test TaskManager.Tests/TaskManager.Tests.csproj
```

Manual smoke checklist:
1. Start app → terminates normally, log file has Debug entries.
2. Kill a process via UI → works silently (no dialog when everything succeeded).
3. Kill with a stale selection (kill an external process externally first, then click Terminate) → warning dialog "{0} of {1} operations succeeded." appears, app alive.
4. Export to a read-only folder → failure dialog, export window stays open.
5. Temporarily rename user.config to force corrupt settings → app starts with defaults + one error dialog (then restore config).

- [ ] **Step 10: Commit**

```bash
git add TaskManager/ViewModels TaskManager/Services/Factories TaskManager/App.xaml.cs TaskManager/TaskManager.csproj TaskManager.Shared/Resources/Languages/Strings.resx TaskManager.Shared/Resources/Languages/Strings.Designer.cs TaskManager.Tests/Viewmodels/DataExportWindowViewModelTests.cs
git commit -m "feat(ui): adopt three-tier error handling across view models"
```

---

## Self-Review Record

1. **Spec coverage:** Tier 1 (§4.1) → Tasks 5–6; Tier 2 (§4.2) → Tasks 2 & 7; Tier 3 (§4.3) → Task 3; fixes §4.4.1–4.4.5 → Tasks 4, 5, 7; logging (§3.1) → Task 1; strings (§3.2) → Tasks 2 & 7; testing matrix (§5) → tasks 1, 2, 4, 5, 6, 7; non-goals respected (no Result monad, no custom exceptions, no async refactor beyond guarding).
2. **Placeholder scan:** none — every code step shows full code; ambiguous first draft of `DataExportWindowViewModelTests` was consolidated into its final form inline.
3. **Type consistency:** `ProcessOpSummary`/`ProcessOpFailure`/`ProcessOpFailureReason` defined Task 5, consumed Task 7 verbatim; `ExportResult`/`ExportFailureReason` defined Task 6, consumed Task 7 verbatim; `IErrorHandler` signatures identical between Task 2 definition, Task 3 consumption, and Task 7 usage; ctor parameter orders consistent between definitions, factory updates, and tests.
