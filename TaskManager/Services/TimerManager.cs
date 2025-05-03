using System;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Task_Manager.Services
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

        public TimerManager(double interval)
        {
            _timer = new Timer();
            Interval = interval;
            _timer.Elapsed += OnTimerElapsed;
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

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}