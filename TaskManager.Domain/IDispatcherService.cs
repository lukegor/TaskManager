using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Domain
{
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
