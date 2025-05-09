using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Utility.Utility
{
    [Flags]
    public enum Preconditions
    {
        None = 0,
        SelectedAnyProcess = 1 << 1,
        GotConfirmation = 1 << 2,
    }
}
