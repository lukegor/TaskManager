using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Domain;

namespace Task_Manager
{
    public class WpfAppSettings : IAppSettings
    {
        public int RefreshRate => Convert.ToInt32(Properties.Settings.Default.RefreshRate);
        public string TimeStampFormat => Properties.Settings.Default.TimeStampFormat;


        public event PropertyChangedEventHandler? SettingChanged;

        public WpfAppSettings()
        {
            Properties.Settings.Default.PropertyChanged += (s, e) =>
            {
                SettingChanged?.Invoke(s, e);
            };
        }
    }

}
