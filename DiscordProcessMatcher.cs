using System;
using System.Collections.Generic;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>Which processes count as Discord. Explicit names only: "starts with Discord" is NOT enough.</summary>
    internal static class DiscordProcessMatcher
    {
        // Exact process names (without ".exe"): stable, PTB, Canary, dev builds and the common third-party clients.
        private static readonly HashSet<string> KnownNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment", "Vesktop",
              "Equibop", "Legcord", "ArmCord", "WebCord", "Vencord", "Dorion" };

        public static bool IsDiscord(string processName, ISet<string> extraNames)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            return KnownNames.Contains(processName) || (extraNames != null && extraNames.Contains(processName));
        }

        /// <summary>Parses the "ExtraProcessNames" registry value: names separated by "," or ";", ".exe" optional.</summary>
        public static HashSet<string> ParseExtraNames(string raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return set;

            foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }
    }
}
