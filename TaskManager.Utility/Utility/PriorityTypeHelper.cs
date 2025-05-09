using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.Utility.Utility
{
    public static class PriorityTypeHelper
    {
        private static readonly Dictionary<ProcessPriorityClass, int> BasePriorityMap = new Dictionary<ProcessPriorityClass, int>
        {
            { ProcessPriorityClass.Idle, 4 },
            { ProcessPriorityClass.BelowNormal, 6 },
            { ProcessPriorityClass.Normal, 8 },
            { ProcessPriorityClass.AboveNormal, 10 },
            { ProcessPriorityClass.High, 13 },
            { ProcessPriorityClass.RealTime, 24 },
        };

        /// <summary>
        /// ProcessPriorityClass enum values to BasePriority
        /// </summary>
        /// <param name="priority"></param>
        /// <returns></returns>
        public static int GetBasePriority(ProcessPriorityClass priority)
        {
            return BasePriorityMap.TryGetValue(priority, out int basePriority)
                ? basePriority
                : (int)priority;
        }

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

        public static string MapEnumToLocalString(ProcessPriorityClass shape)
        {
            return EnumHelper.MapEnumToLocalString(shape, ProcessPriorityTypeMapping);
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
