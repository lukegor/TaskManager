using System.Globalization;

namespace TaskManager.Domain.Primitives
{
    public class LanguageDictionary : Dictionary<string, CultureInfo>
    {
        public static IReadOnlyList<string> KeysList { get; } =
            new LanguageDictionary().Keys.ToList().AsReadOnly();
        public LanguageDictionary()
        {
            Add("English", new CultureInfo("en"));
            Add("polski", new CultureInfo("pl"));
        }
    }
}
