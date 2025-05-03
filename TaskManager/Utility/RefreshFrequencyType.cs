using DocumentFormat.OpenXml.Vml;
using Task_Manager.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Task_Manager.Utility
{
    public enum RefreshFrequencyType
    {
        High,
        Medium,
        Low,
        Paused,
    }

    public static class RefreshFrequencyTypeHelper
    {
        public static readonly Dictionary<RefreshFrequencyType, int> RefreshFrequencyTypeSecondsMapping = new Dictionary<RefreshFrequencyType, int>
        {
            { RefreshFrequencyType.High, 5},
            { RefreshFrequencyType.Medium,  7},
            { RefreshFrequencyType.Low, 10},
            { RefreshFrequencyType.Paused, 0},
        };

        private static readonly Dictionary<string, RefreshFrequencyType> RefreshFrequencyTypeMapping = new Dictionary<string, RefreshFrequencyType>
        {
            { Strings.High, RefreshFrequencyType.High },
            { Strings.Medium, RefreshFrequencyType.Medium },
            { Strings.Low, RefreshFrequencyType.Low },
            { Strings.Paused, RefreshFrequencyType.Paused },
        };

        public static RefreshFrequencyType MapLocalStringToEnum(string input)
        {
            return EnumHelper.MapLocalStringToEnum(input, RefreshFrequencyTypeMapping);
        }

        public static string MapEnumToLocalString(RefreshFrequencyType shape)
        {
            return EnumHelper.MapEnumToLocalString(shape, RefreshFrequencyTypeMapping);
        }

        public static IEnumerable<string> GetAllLocalized()
        {
            return EnumHelper.GetAllLocalizedOptions(RefreshFrequencyTypeMapping);
        }

        public static IEnumerable<string> GetLocalizedShapeTypes(IEnumerable<RefreshFrequencyType> frequencyTypes)
        {
            return EnumHelper.GetLocalizedOptions(frequencyTypes, RefreshFrequencyTypeMapping);
        }


    }
}
