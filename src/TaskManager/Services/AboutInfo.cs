using System.Reflection;

namespace TaskManager.Services
{
    /// <summary>Parses the stamped InformationalVersion (&lt;version&gt;+&lt;sha&gt;) for the About dialog.</summary>
    internal static class AboutInfo
    {
        public static string Version { get; } = Parse(InformationalVersion()).Version;
        public static string Commit { get; } = Parse(InformationalVersion()).Commit;

        internal static (string Version, string Commit) Parse(string informationalVersion)
        {
            var separatorIndex = informationalVersion.IndexOf('+');
            return separatorIndex < 0
                ? (informationalVersion, string.Empty)
                : (informationalVersion[..separatorIndex], informationalVersion[(separatorIndex + 1)..]);
        }

        private static string InformationalVersion() =>
            Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
            ?? "0.0.0";
    }
}
