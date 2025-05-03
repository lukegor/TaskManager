using Task_Manager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Task_Manager.Services
{
    internal abstract class BaseDataExporter
    {
	    protected const string FileNamePrefix = @"\record-";
	    protected readonly string DateTime = Properties.Settings.Default.TimeStampFormat;
        protected abstract string Extension { get; }

		public void Export<T>(string dirPath, IEnumerable<T> records) where T: IExportable
        {
            IList<string> strings = GetStrings(records).ToList();

            string fullFileName = dirPath + GenerateFileName(Extension);
            PerformExport<T>(fullFileName, strings);
        }

        protected abstract void PerformExport<T>(string fullFileName, IEnumerable<string> strings);

        protected virtual IEnumerable<string> GetStrings<T>(IEnumerable<T> eventRecords) where T: IExportable
        {
	        foreach (T record in eventRecords)
	        {
		        yield return record.ToDelimitedString(' ');
	        }
        }

        protected string GenerateFileName(string extension)
        {
            return $"{FileNamePrefix}{System.DateTime.Now.ToString(DateTime)}.{extension}";
        }
    }
}
