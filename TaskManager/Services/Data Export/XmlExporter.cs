using Task_Manager.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Task_Manager.Services.Data_Export
{
	internal class XmlExporter : BaseDataExporter
	{
		protected override string Extension => "xml";

        protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
        {
            var root = new XElement("Records",
                strings.Select(record =>
                    new XElement("Record",
                        typeof(T).GetProperties()
                            .Where(prop => !prop.IsDefined(typeof(IgnoreSerialization), false))
                            .Select(prop => new XElement(prop.Name, prop.GetValue(record)?.ToString() ?? string.Empty)
                            )
                    )
                )
            );

            var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
            xmlDoc.Save(fullFileName);
        }
    }
}
