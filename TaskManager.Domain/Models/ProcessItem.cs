namespace TaskManager.Domain.Models
{
    /// <summary>
    /// Models.Process wrapper with wider logic
    /// </summary>
    public class ProcessItem
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
