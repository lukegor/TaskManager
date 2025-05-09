using CommunityToolkit.Mvvm.ComponentModel;
using NtApiDotNet;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TaskManager.Domain;
using TaskManager.Domain.Models;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services
{
    public class ProcessManager : ObservableObject
    {
        private ObservableCollection<ProcessItem> processes = new ObservableCollection<ProcessItem>();
        public ObservableCollection<ProcessItem> Processes
        {
            get { return processes; }
            set
            {
                SetProperty(ref processes, value);
            }
        }

        private int processCount;
        public int ProcessCount
        {
            get { return processes.Count; }
            set { SetProperty(ref processCount, value); }
        }

        public List<ProcessItem> GetProcessList() {
            return processes.ToList();
        }

        private readonly IDispatcherService _dispatcher;
        private readonly IAppSettings _settings;
        private readonly TimerManager _timer;

        // Event handler to update the field when the setting changes.
        private void Default_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_settings.RefreshRate))
            {
                var mappingKey = (RefreshFrequencyType)_settings.RefreshRate;
                _timer.UpdatePolling(RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[mappingKey]);
            }
        }

        public ProcessManager(IDispatcherService dispatcher, IAppSettings settings, TimerManager timerManager)
        {
            _dispatcher = dispatcher;
            _settings = settings;
            _timer = timerManager;

            Processes.CollectionChanged += Processes_CollectionChanged;
            _settings.SettingChanged += Default_PropertyChanged;

            _timer.Elapsed += OnProcessPolling;
        }

		public void Processes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ProcessCount = Processes.Count;
            OnPropertyChanged(nameof(Processes));
		}

        /// <summary>
        ///
        /// </summary>
        /// <remarks>It's much faster thanks to asynchronous loading</remarks>
        public async void LoadProcesses()
        {
            var processList = await Task.Run(() => GetProcesses());

            foreach (var process in processList)
            {
	            Processes.Add(new ProcessItem(process));
            }
        }

		public void StartPollingProcesses()
		{
            _timer.Start();
		}

		private async void OnProcessPolling(object sender, System.Timers.ElapsedEventArgs e)
		{
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] Polling");
            await PerformRefresh(sender);
		}

        public async Task PerformRefresh(object sender)
        {
            await Refresh();

            if (sender is Button b)
            {
                _timer.Restart();
            }
        }

        private async Task Refresh()
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] REFRESHING processes");
            List<Process> actualProcesses = await Task.Run(() => GetProcesses().ToList());

            List<ProcessItem> deletedProcesses = GetProcessList()
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

        public void TerminateProcesses(IEnumerable<System.Diagnostics.Process> selectedProcesses)
        {
            foreach (var process in selectedProcesses)
            {
                process.Kill();
                System.Diagnostics.Debug.WriteLine($"Process {process.Id} was terminated");
            }
        }

        public void SetPriority(IEnumerable<System.Diagnostics.Process> selectedProcesses, System.Diagnostics.ProcessPriorityClass priority)
        {
            foreach (var process in selectedProcesses)
            {
                process.PriorityClass = priority;
                var storedProcess = Processes.FirstOrDefault(p => p.Process.Pid == process.Id).Process;
                storedProcess.Priority = PriorityTypeHelper.GetBasePriority(priority);
                System.Diagnostics.Debug.WriteLine($"Process {process.Id} priority set to {priority}");
            }

            _dispatcher.Invoke(() =>
            {
                var view = CollectionViewSource.GetDefaultView(Processes);
                view?.Refresh();
            });
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// In order to get some processes, it requires Admin privileges and in some cases, even this isn't enough
        /// TO-DO: change processInfo retrieving method so that it returns ALL processes
        /// </remarks>
        private static IEnumerable<Process> GetProcesses()
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
                            Path = process.MainModule.FileName,
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
                    //System.Diagnostics.Debug.WriteLine(ex.ToString());
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
                System.Diagnostics.Debug.WriteLine($"IsWow64Process failed to determine process bitness.\nError code: {errorCode}");
                return false;
            }
        }
    }
}
