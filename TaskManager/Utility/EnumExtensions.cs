using System.ComponentModel;
using System.Reflection;

namespace Task_Manager.Utility
{
    internal static class EnumExtensions
    {
        public static string ToString<T>(this T enumValue) where T : Enum
        {
            Type type = enumValue.GetType();

            FieldInfo field = type.GetField(enumValue.ToString());

            // get Description attribute from FieldInfo
            DescriptionAttribute attribute =
                (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));

			// return field description; if it's absent return default name
			return attribute == null ? enumValue.ToString() : attribute.Description;
        }
    }
}
