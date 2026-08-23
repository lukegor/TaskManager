using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NtApiDotNet;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services
{
    public class ProcessManager : ObservableObject
    {
        public ObservableCollection<ProcessItem> Processes { get; set => SetProperty(ref field, value); } = new();

        /// <summary>The getter reports the live collection count; the setter exists only to raise PropertyChanged.</summary>
        public int ProcessCount
        {
            get => Processes.Count;
            set => OnPropertyChanged();
        }

        private readonly IDispatcherService _dispatcher;
        private readonly ISettingsService _settings;
        private readonly TimerManager _timer;
        private readonly ILogger<ProcessManager> _logger;

        public ProcessManager(IDispatcherService dispatcher, ISettingsService settings, TimerManager timerManager,
            ILogger<ProcessManager> logger)
        {
            _dispatcher = dispatcher;
            _settings = settings;
            _timer = timerManager;
            _logger = logger;

            Processes.CollectionChanged += Processes_CollectionChanged;

            _timer.Elapsed += OnProcessPolling;

            // Live-apply refresh-frequency changes; the event is declared on the
            // interface, so this wiring is compiler-enforced.
            _settings.Changed += OnSettingsChanged;
        }

        private void OnSettingsChanged(AppSettings settings)
        {
            var seconds = RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[settings.ProcessesRefreshFrequency];
            _timer.UpdatePolling(seconds);
            _logger.LogInformation("Polling interval updated to {Seconds}s", seconds);
        }

        public void Processes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ProcessCount = Processes.Count;
            OnPropertyChanged(nameof(Processes));
        }

        /// <summary>
        /// Enumerates all processes off-thread, then populates the collection in one UI-thread batch.
        /// </summary>
        /// <remarks>
        /// The enumeration must be materialized (<c>ToList</c>) on the worker thread: <see cref="GetProcesses"/>
        /// is a lazy iterator, so handing it to <see cref="Task.Run"/> directly would run the expensive
        /// enumeration inside the consumer loop instead. Adds are marshalled through the dispatcher because
        /// the collection is bound to the UI; cross-thread collection changes never reach the grid.
        /// </remarks>
        public async Task LoadProcesses()
        {
            var processList = await Task.Run(() => GetProcesses().ToList()).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                foreach (var process in processList)
                {
                    Processes.Add(new ProcessItem(process));
                }
            });
        }

        public void StartPollingProcesses()
        {
            _timer.Start();
        }

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
                // async void callback: an escaping exception would terminate the process
                _logger.LogWarning(ex, "Polling refresh failed");
            }
        }

        public async Task PerformRefresh(bool isUserInitiated)
        {
            await Refresh();

            if (isUserInitiated)
            {
                _timer.Restart();
            }
        }

        private async Task Refresh()
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] REFRESHING processes");
            List<Process> actualProcesses = await Task.Run(() => GetProcesses().ToList());

            List<ProcessItem> deletedProcesses = Processes
                .Where(existingProcess => !actualProcesses.Any(p => p.Pid == existingProcess.Process.Pid))
                .ToList();

            foreach (var process in deletedProcesses)
            {
                _dispatcher.Invoke(() =>
                    Processes.Remove(process)
                );
            }

            foreach (var newProcess in actualProcesses)
            {
                if (!Processes.Any(p => p.Process.Pid == newProcess.Pid))
                {
                    try
                    {
                        _dispatcher.Invoke(() =>
                            Processes.Add(new ProcessItem(newProcess))
                        );
                    }
                    catch (TaskCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("Polling update task cancelled");
                    }
                }
            }
        }

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

        private const int ErrorAccessDenied = 5;

        private static ProcessOpFailureReason ClassifyFailure(Exception ex) => ex switch
        {
            Win32Exception { NativeErrorCode: ErrorAccessDenied } => ProcessOpFailureReason.AccessDenied,
            ArgumentException => ProcessOpFailureReason.ProcessExited,
            InvalidOperationException => ProcessOpFailureReason.ProcessExited,
            _ => ProcessOpFailureReason.Unknown
        };

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// In order to get some processes, it requires Admin privileges and in some cases, even this isn't enough
        /// TO-DO: change processInfo retrieving method so that it returns ALL processes
        /// </remarks>
        private IEnumerable<Process> GetProcesses()
        {
            var allProcesses = System.Diagnostics.Process.GetProcesses();

            foreach (var process in allProcesses)
            {
                Process processInfo;
                try
                {
                    bool? result = null;
                    if (TryGetProcessBitness(process.Handle, out bool is32Bit))
                    {
                        result = is32Bit;
                    }

                    using (var ntProcess = NtProcess.FromHandle(process.Handle))
                    {
                        processInfo = new Process
                        {
                            Name = process.ProcessName,
                            Pid = process.Id,
                            Path = process.MainModule?.FileName ?? string.Empty,
                            ArchitectureType = result switch
                            {
                                true => ArchitectureType._32BIT,
                                false => ArchitectureType._64BIT,
                                null => ArchitectureType.Unknown
                            },
                            Priority = process.BasePriority,
                            ThreadCount = process.Threads.Count,
                            Ppid = ntProcess.ParentProcessId,
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipped inaccessible process during enumeration");
                    continue;
                }
                yield return processInfo;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(nint hProcess, out bool wow64Process);

        private static bool TryGetProcessBitness(nint processHandle, out bool is32Bit)
        {
            if (IsWow64Process(processHandle, out is32Bit)) { return true; }
            else
            {
                int errorCode = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"IsWow64Process failed to determine pId bitness.\nError code: {errorCode}");
                return false;
            }
        }
    }
}
