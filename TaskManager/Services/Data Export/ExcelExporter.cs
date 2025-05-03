using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ClosedXML.Excel;
using Task_Manager.Models;
using Task_Manager.Utility;

namespace Task_Manager.Services.Data_Export
{
    internal class ExcelExporter : BaseDataExporter
    {
	    private const char Separator = ';';
        protected override string Extension => "xlsx";

        protected override IEnumerable<string> GetStrings<T>(IEnumerable<T> eventRecords)
        {
	        foreach (T record in eventRecords)
	        {
		        yield return record.ToDelimitedString(Separator);
	        }
        }

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
			List<string> stringList = strings.ToList();

            var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Records");

            string[] headers = GetColumnHeaders(typeof(T));
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            for (int i = 0; i < stringList.Count; i++)
            {
                var columns = stringList[i].Split(Separator);

                for (int j = 0; j < columns.Length; j++)
                {
                    worksheet.Cell(i + 2, j + 1).Value = columns[j];
                }
            }

            IXLRange range = worksheet.Range(1, 1, stringList.Count + 1, stringList.FirstOrDefault()?.Split(Separator).Length ?? 0);
            IXLTable table = range.CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium9;
            table.ShowAutoFilter = true;
            table.Name = "Records";

            workbook.SaveAs(fullFileName);
        }

        private string[] GetColumnHeaders(Type t)
		{
			PropertyInfo[] properties = t.GetProperties()
				.Where(prop => !prop.IsDefined(typeof(IgnoreSerialization), false) &&
										!prop.IsDefined(typeof(JsonIgnoreAttribute), false))
				.ToArray();

			string[] headers = properties.Select(prop => prop.Name).ToArray();

			return headers;
		}
	}
}
