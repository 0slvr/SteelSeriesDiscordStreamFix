using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SteelSeriesDiscordStreamFix
{
    internal static class Logger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteelSeriesDiscordStreamFix-Data");

        private static readonly string LogPath = Path.Combine(LogDir, "log.txt");
        private static readonly string LogPathOld = Path.Combine(LogDir, "log.old.txt");
        private const long MaxLogSizeBytes = 1 * 1024 * 1024; // 1 MB cap

        private static readonly object LockObj = new object();

        // Logs get attached to public issues: never write the Windows user name (it is part of every profile path).
        private static readonly string ProfilePath = SafeProfilePath();

        // Windows paths are case-insensitive: "c:\users\name" must be redacted just like "C:\Users\Name".
        // Also covers the forward-slash form of the same path.
        private static readonly Regex ProfileRegex = BuildProfileRegex(ProfilePath);

        private static Regex BuildProfileRegex(string profile)
        {
            try
            {
                if (string.IsNullOrEmpty(profile)) return null;
                string trimmed = profile.TrimEnd('\\', '/');
                if (trimmed.Length == 0) return null;
                string pattern = Regex.Escape(trimmed).Replace("\\\\", "[\\\\/]");
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            catch { return null; }
        }

        private static string SafeProfilePath()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            catch { return null; }
        }

        /// <summary>One line with the facts a bug report needs: app version, Windows build, bitness, runtime.</summary>
        public static void LogHeader()
        {
            try
            {
                Version v = typeof(Logger).Assembly.GetName().Version;
                Log("=== SteelSeriesDiscordStreamFix " + v + " | " + Environment.OSVersion.VersionString +
                    " | 64-bit OS=" + Environment.Is64BitOperatingSystem + " | CLR " + Environment.Version + " ===");
            }
            catch { }
        }

        public static string LogFilePath => LogPath;

        /// <summary>
        /// Removes the log folder. Only used by the uninstall hook: the logs now live outside Velopack's
        /// install folder, so the uninstaller no longer deletes them by itself.
        /// </summary>
        public static void DeleteLogFolder()
        {
            try
            {
                lock (LockObj)
                {
                    if (Directory.Exists(LogDir)) Directory.Delete(LogDir, true);
                }
            }
            catch
            {
                // Best effort.
            }
        }

        public static void OpenLogFile()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                if (!File.Exists(LogPath)) File.WriteAllText(LogPath, string.Empty);
                Process.Start(new ProcessStartInfo { FileName = LogPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("OpenLogFile failed: " + ex.Message);
            }
        }

        public static void Log(string message)
        {
            try
            {
                if (ProfileRegex != null && message != null)
                    message = ProfileRegex.Replace(message, "%USERPROFILE%");

                lock (LockObj)
                {
                    Directory.CreateDirectory(LogDir);
                    RotateIfTooBig();
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging itself must never crash the app.
            }
        }

        private static void RotateIfTooBig()
        {
            try
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxLogSizeBytes)
                {
                    File.Copy(LogPath, LogPathOld, overwrite: true);
                    File.Delete(LogPath);
                }
            }
            catch
            {
                // If the check/rotate fails, just keep appending.
            }
        }
    }
}