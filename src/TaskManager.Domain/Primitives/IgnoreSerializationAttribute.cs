namespace TaskManager.Domain.Primitives
{
	[AttributeUsage(AttributeTargets.Property)]
	public class IgnoreSerializationAttribute : Attribute
	{
	}
}
