namespace TaskManager.Domain.Models
{
    public interface IExportable
	{
		public abstract string ToDelimitedString(char separator);
	}
}
