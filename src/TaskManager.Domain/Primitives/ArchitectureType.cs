using System.ComponentModel;

namespace TaskManager.Domain.Primitives
{
    public enum ArchitectureType
    {
        [Description("32-bit")]
        Bit32,
        [Description("64-bit")]
        Bit64,
        [Description("Unknown")]
        Unknown
    }
}
