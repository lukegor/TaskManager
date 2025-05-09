using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Domain;

namespace Task_Manager
{
    public class WpfDispatcherService : IDispatcherService
    {
        public void Invoke(Action action)
        {
            App.Current.Dispatcher.Invoke(action);
        }
    }
}
