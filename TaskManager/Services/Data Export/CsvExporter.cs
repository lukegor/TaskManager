using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Task_Manager.Models;
using Microsoft.VisualBasic;

namespace Task_Manager.Services.Data_Export
{
    internal class CsvExporter : BaseDataExporter
    {
	    private const char Separator = ',';
        protected override string Extension => "csv";

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            File.WriteAllLines(fullFileName, strings);
        }

        protected override IEnumerable<string> GetStrings<T>(IEnumerable<T> eventRecords)
        {
	        foreach (T record in eventRecords)
	        {
		        yield return record.ToDelimitedString(Separator);
	        }
        }
	}
}
