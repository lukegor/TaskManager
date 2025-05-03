using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Task_Manager.Utility;

namespace Task_Manager.Models
{
    /// <summary>
    /// Represents Windows System Process data (core business logic)
    /// </summary>
    internal class Process : IExportable
	{
        public string Name { get; set; }
        public int? Pid { get; set; }
        [IgnoreSerialization]
        [JsonIgnore]
        public ArchitectureType ArchitectureType { get; set; }
        public string Path { get; set; }
        public int? Priority { get; set; }
        public int ThreadCount { get; set; }
        public int? Ppid { get; set; }
        [IgnoreSerialization]
        [JsonIgnore]
        public string ArchitectureTypeDisplay => EnumExtensions.ToString(ArchitectureType);

        public override string ToString()
        {
            return $"{Name} ({Pid})";
        }

        public string ToDelimitedString(char separator)
        {
	        return string.Join(separator.ToString(),
                Name,
                Pid,
                Path,
                Priority,
                ThreadCount,
                Ppid);
        }
	}
}
