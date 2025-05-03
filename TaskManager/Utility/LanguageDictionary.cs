using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Task_Manager.Utility
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
