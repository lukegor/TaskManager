using System.Timers;
using TaskManager.Domain.Abstractions;
using TaskManager.Utility.Utility;
using Timer = System.Timers.Timer;

namespace TaskManager.Domain.Services
{
    public class TimerManager : IDisposable
    {
        private readonly Timer _timer;

        public event ElapsedEventHandler? Elapsed;

        public double Interval
        {
            get;
            set
            {
                field = value;
                if (value == 0)
                {
                    Stop();
                    return;
                }
                _timer.Interval = value;
            }
        }

        const int MilisecondMultiplier = 1000;

        private readonly ISettingsService _settings;

        public TimerManager(ISettingsService settings)
        {
            _settings = settings;

            _timer = new Timer();
            Interval = MilisecondMultiplier * RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping
                [_settings.Current.ProcessesRefreshFrequency];
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            Elapsed?.Invoke(sender, e);
        }

        public bool Start()
        {
            if (Interval == 0)
            {
                return false;
            }

            _timer.Start();
            return true;
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        public void UpdatePolling(int seconds)
        {
            Interval = MilisecondMultiplier * seconds;
        }
    }
}