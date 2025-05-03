using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Task_Manager.Models
{
	/// <summary>
	/// Models.Process wrapper with wider logic
	/// </summary>
	internal class ProcessItem
	{
		public Process Process { get; set; }
		public bool IsSelected { get; set; }


        public ProcessItem()
		{
			IsSelected = false;
		}

		public ProcessItem(Process process) : this()
		{
			Process = process;
		}

		public override string ToString()
		{
			return $"{Process.Name} ({Process.Pid})";
		}
	}
}
