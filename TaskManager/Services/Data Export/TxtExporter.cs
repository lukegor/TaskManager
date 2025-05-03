using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Task_Manager.Models;

namespace Task_Manager.Services.Data_Export
{
    internal class TxtExporter : BaseDataExporter
    {
        protected override string Extension => "txt";

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }
    }
}
