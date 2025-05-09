using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Domain.Models
{
    public interface IExportable
	{
		public abstract string ToDelimitedString(char separator);
	}
}
