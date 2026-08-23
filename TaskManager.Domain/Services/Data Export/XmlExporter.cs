using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TaskManager.Domain.Abstractions;
using TaskManager.Utility.Utility;

namespace TaskManager.Domain.Services.Data_Export
{
	public class XmlExporter : BaseDataExporter
	{
		protected override string Extension => "xml";

        public XmlExporter(IAppSettings settings, ILogger<BaseDataExporter> logger) : base(settings, logger)
        {
        }

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
