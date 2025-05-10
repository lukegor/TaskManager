using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Tests.Integration_Tests.ProcessManagementTests
{
    public class ProcessesData
    {
        public static IEnumerable<System.Diagnostics.Process> GetProcesses =>
            new List<System.Diagnostics.Process>
            {
                //new System.Diagnostics.Process
                //{
                //    Id = 1,
                //    ProcessName = "Process1",
                //    StartTime = DateTime.Now.AddMinutes(-10),
                //    TotalProcessorTime = TimeSpan.FromMinutes(5)
                //},
                //new System.Diagnostics.Process
                //{
                //    Id = 2,
                //    ProcessName = "Process2",
                //    StartTime = DateTime.Now.AddMinutes(-20),
                //    TotalProcessorTime = TimeSpan.FromMinutes(10)
                //},
                //new System.Diagnostics.Process
                //{
                //    Id = 3,
                //    ProcessName = "Process3",
                //    StartTime = DateTime.Now.AddMinutes(-30),
                //    TotalProcessorTime = TimeSpan.FromMinutes(15)
                //}
            };
    }
}
