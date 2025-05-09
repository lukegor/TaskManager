using System.ComponentModel;

namespace TaskManager.Utility.Utility
{
    public enum ArchitectureType
    {
        [Description("32-bit")]
        _32BIT,
        [Description("64-bit")]
        _64BIT,
        [Description("Unknown")]
        Unknown
    }
}
