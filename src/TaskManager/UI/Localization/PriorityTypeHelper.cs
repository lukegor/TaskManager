using System.Diagnostics;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.UI.Localization
{
    public static class PriorityTypeHelper
    {
        private static readonly Dictionary<string, ProcessPriorityClass> ProcessPriorityTypeMapping = new Dictionary<string, ProcessPriorityClass>
        {
            { Strings.RealTime, ProcessPriorityClass.RealTime },
            { Strings.High_m, ProcessPriorityClass.High },
            { Strings.AboveNormal, ProcessPriorityClass.AboveNormal },
            { Strings.Normal, ProcessPriorityClass.Normal },
            { Strings.BelowNormal, ProcessPriorityClass.BelowNormal },
            { Strings.Idle, ProcessPriorityClass.Idle },
        };

        public static ProcessPriorityClass MapLocalStringToEnum(string input)
        {
            return EnumHelper.MapLocalStringToEnum(input, ProcessPriorityTypeMapping);
        }

        public static string MapEnumToLocalString(ProcessPriorityClass priority)
        {
            return EnumHelper.MapEnumToLocalString(priority, ProcessPriorityTypeMapping);
        }

        public static IEnumerable<string> GetAllLocalized()
        {
            return EnumHelper.GetAllLocalizedOptions(ProcessPriorityTypeMapping);
        }

        public static IEnumerable<string> GetLocalized(IEnumerable<ProcessPriorityClass> priorityTypes)
        {
            return EnumHelper.GetLocalizedOptions(priorityTypes, ProcessPriorityTypeMapping);
        }
    }
}
