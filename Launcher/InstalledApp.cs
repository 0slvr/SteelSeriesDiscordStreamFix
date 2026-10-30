using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    /// <summary>Finds and controls the copy of the app that Velopack installed for this Windows user.</summary>
    internal sealed class InstalledApp
    {
        public const string AppId = "SteelSeriesDiscordStreamFix";
        public const string ExeName = "SteelSeriesDiscordStreamFix.exe";

        // Must match Program.cs in the app.
        private const string ExitRequestEventName = "SteelSeriesDiscordStreamFix_ExitRequest_Event";

        public static readonly string RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppId);

        public static readonly string ExePath = Path.Combine(RootDir, "current", ExeName);
        public static readonly string UpdateExePath = Path.Combine(RootDir, "Update.exe");

        public Version Version { get; private set; }

        /// <summary>Null when the app is not installed (or the install is incomplete).</summary>
        public static InstalledApp Detect()
        {
            try
            {
                if (!File.Exists(ExePath) || !File.Exists(UpdateExePath)) return null;

                var fvi = FileVersionInfo.GetVersionInfo(ExePath);
                return new InstalledApp { Version = new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart) };
            }
            catch (Exception ex)
            {
                Log.Write("Detect failed: " + ex.Message);
                return null;
            }
        }

        public static bool IsRunning()
        {
            Process[] list = Process.GetProcessesByName(AppId);
            try { return list.Length > 0; }
            finally { foreach (var p in list) p.Dispose(); }
        }

        /// <summary>
        /// Asks a running copy to quit through its normal exit path (un-mutes Discord, restores the Sonar
        /// devices it hid) and waits a few seconds. Older versions without this feature just keep running;
        /// Velopack's Setup then replaces them anyway.
        /// </summary>
        public static void RequestExitAndWait(int timeoutMs)
        {
            if (!IsRunning()) return;

            try
            {
                using (var ev = EventWaitHandle.OpenExisting(ExitRequestEventName))
                    ev.Set();
            }
            catch (Exception ex)
            {
                Log.Write("Exit request not delivered (older version?): " + ex.Message);
            }

            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (IsRunning() && DateTime.UtcNow < until) Thread.Sleep(200);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        /// <summary>Starts the installed app. If it is already running it just shows its Settings window.</summary>
        public static void Launch(string arguments)
        {
            // The launcher was just clicked, so it may hand the right to come to the front to the app it starts.
            try { AllowSetForegroundWindow(-1 /* ASFW_ANY */); } catch { }

            Process.Start(new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(ExePath),
                UseShellExecute = false
            });
        }
    }

    internal static class Log
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteelSeriesDiscordStreamFix-Data");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "launcher-log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { /* logging must never break the launcher */ }
        }
    }
}
