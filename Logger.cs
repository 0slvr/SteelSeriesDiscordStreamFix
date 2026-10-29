using System;
using System.IO;

namespace SteelSeriesDiscordStreamFix
{
    internal static class Logger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteelSeriesDiscordStreamFix");

        private static readonly string LogPath = Path.Combine(LogDir, "log.txt");
        private static readonly string LogPathOld = Path.Combine(LogDir, "log.old.txt");
        private const long MaxLogSizeBytes = 1 * 1024 * 1024; // 1 MB cap

        private static readonly object LockObj = new object();

        public static void Log(string message)
        {
            try
            {
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