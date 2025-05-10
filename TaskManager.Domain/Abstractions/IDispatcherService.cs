using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Domain.Abstractions
{
    public interface IDispatcherService
    {
        void Invoke(Action action);
    }
}
