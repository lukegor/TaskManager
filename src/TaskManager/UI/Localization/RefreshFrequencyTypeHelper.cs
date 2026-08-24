using TaskManager.Resources.Languages;
using TaskManager.Domain.Primitives;

namespace TaskManager.UI.Localization
{
    public static class RefreshFrequencyTypeHelper
    {
        private static readonly Dictionary<string, RefreshFrequencyType> LocalizedMapping = new Dictionary<string, RefreshFrequencyType>
        {
            { Strings.High, RefreshFrequencyType.High },
            { Strings.Medium, RefreshFrequencyType.Medium },
            { Strings.Low, RefreshFrequencyType.Low },
            { Strings.Paused, RefreshFrequencyType.Paused },
        };

        public static RefreshFrequencyType MapLocalStringToEnum(string input)
        {
            return EnumHelper.MapLocalStringToEnum(input, LocalizedMapping);
        }

        public static string MapEnumToLocalString(RefreshFrequencyType frequency)
        {
            return EnumHelper.MapEnumToLocalString(frequency, LocalizedMapping);
        }

        public static IEnumerable<string> GetAllLocalized()
        {
            return EnumHelper.GetAllLocalizedOptions(LocalizedMapping);
        }

        public static IEnumerable<string> GetLocalizedShapeTypes(IEnumerable<RefreshFrequencyType> frequencyTypes)
        {
            return EnumHelper.GetLocalizedOptions(frequencyTypes, LocalizedMapping);
        }
    }
}
