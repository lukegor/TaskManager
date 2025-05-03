using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Task_Manager.Models
{
	internal interface IExportable
	{
		public abstract string ToDelimitedString(char separator);
	}
}
