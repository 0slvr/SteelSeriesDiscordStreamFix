using System.Text.RegularExpressions;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Replaces the Windows profile path (which contains the user name) in log text. Case-insensitive, and it
    /// matches both "\" and "/" separators. Logs get attached to public issues.
    /// </summary>
    internal sealed class LogRedactor
    {
        private const string Replacement = "%USERPROFILE%";
        private readonly Regex _regex;

        public LogRedactor(string profilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(profilePath)) return;
                string trimmed = profilePath.TrimEnd('\\', '/');
                if (trimmed.Length == 0) return;
                string pattern = Regex.Escape(trimmed).Replace("\\\\", "[\\\\/]");
                _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            catch
            {
                _regex = null;
            }
        }

        public string Redact(string message)
        {
            if (_regex == null || message == null) return message;
            return _regex.Replace(message, Replacement);
        }
    }
}
