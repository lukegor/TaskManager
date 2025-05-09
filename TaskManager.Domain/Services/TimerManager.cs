using System;
using System.Timers;
using TaskManager.Utility.Utility;
using Timer = System.Timers.Timer;

namespace TaskManager.Domain.Services
{
    public class TimerManager : IDisposable
    {
        private Timer _timer;

        public event ElapsedEventHandler Elapsed;

        private double _intervalValue;
        public double Interval
        {
            get {  return _intervalValue; }
            set
            {
                _intervalValue = value;
                if (value == 0)
                {
                    Stop();
                    return;
                }
                _timer.Interval = value;
            }
        }

        const int MilisecondMultiplier = 1000;

        private readonly IAppSettings _settings;

        public TimerManager(IAppSettings settings)
        {
            _settings = settings;

            _timer = new Timer();
            Interval = MilisecondMultiplier * RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping
                [(RefreshFrequencyType)_settings.RefreshRate];
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
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