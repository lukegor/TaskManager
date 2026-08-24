using System.Globalization;

namespace TaskManager.Domain.Primitives
{
    public class LanguageDictionary : Dictionary<string, CultureInfo>
    {
        public static IEnumerable<string> KeysList => new LanguageDictionary().Keys;
        public LanguageDictionary()
        {
            Add("English", new CultureInfo("en"));
            Add("polski", new CultureInfo("pl"));
        }
    }
}
