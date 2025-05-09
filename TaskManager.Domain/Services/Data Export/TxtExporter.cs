using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Domain;
using TaskManager.Domain.Models;

namespace Task_Manager.Services.Data_Export
{
    public class TxtExporter : BaseDataExporter
    {
        protected override string Extension => "txt";

        public TxtExporter(IAppSettings settings) : base(settings)
        {
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }
    }
}
