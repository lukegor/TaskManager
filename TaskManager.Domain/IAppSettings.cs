using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Domain
{
    public interface IAppSettings
    {
        int RefreshRate { get; }
        string TimeStampFormat { get; }
        // Add more as needed

        event PropertyChangedEventHandler? SettingChanged;
    }
}
