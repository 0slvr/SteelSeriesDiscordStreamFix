using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    internal sealed class LauncherSettings
    {
        public bool HideSonarChatAndGaming { get; set; }
        public bool StartWithWindows { get; set; }
    }

    internal static class Installer
    {
        // Same registry layout the app reads (AppSettings.cs).
        private const string RegistryPath = @"SOFTWARE\SteelSeriesDiscordStreamFix";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SteelSeriesDiscordStreamFix";

        public static bool HasEmbeddedSetup()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.exe"))
                return s != null;
        }

        /// <summary>
        /// Installs (or updates) the app with the embedded Velopack Setup.exe and starts it quietly.
        /// <paramref name="settings"/> is null for an update: the user's saved options stay as they are.
        /// Returns null on success, otherwise a message for the user. Blocking: call it from a worker thread.
        /// </summary>
        public static string Install(LauncherSettings settings)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SteelSeriesDiscordStreamFix-setup-" + Guid.NewGuid().ToString("N"));
            bool freshInstall = settings != null;

            try
            {
                Directory.CreateDirectory(tempDir);
                string setupPath = Path.Combine(tempDir, "SteelSeriesDiscordStreamFix-win-Setup.exe");
                using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.exe"))
                {
                    if (res == null) return "This build does not contain the installer (development build).";
                    using (var file = File.Create(setupPath)) res.CopyTo(file);
                }

                // 1. Save the options first so the app already has them when it starts for the first time.
                if (settings != null) WriteSettings(settings);

                // 2. Close a running copy cleanly (it restores the devices it hid and un-mutes Discord).
                InstalledApp.RequestExitAndWait(8000);

                // 3. Velopack's own Setup, silent. After "--" come the arguments for the app it starts:
                //    --autostart = start in the background without opening the Settings window.
                Directory.CreateDirectory(Log.Dir);
                string setupLog = Path.Combine(Log.Dir, "setup-log.txt");
                var psi = new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = "--silent --log \"" + setupLog + "\" -- --autostart",
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Log.Write("Running embedded Setup: " + psi.Arguments);
                Version mine = Assembly.GetExecutingAssembly().GetName().Version;
                var target = new Version(mine.Major, mine.Minor, mine.Build);

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return "The installer could not be started.";

                    // Normally Setup exits after it started the app. Do not hang if it stays open: once the new
                    // version is installed AND running, the job is done.
                    var deadline = DateTime.UtcNow.AddMinutes(3);
                    while (true)
                    {
                        if (proc.HasExited)
                        {
                            Log.Write("Setup exit code: " + proc.ExitCode);
                            if (proc.ExitCode != 0)
                                return "The installer failed (exit code " + proc.ExitCode + "). Details: " + setupLog;
                            break;
                        }

                        InstalledApp now = InstalledApp.Detect();
                        if (now != null && now.Version >= target && InstalledApp.IsRunning())
                        {
                            Log.Write("App is installed and running while Setup is still open; continuing.");
                            break;
                        }

                        if (DateTime.UtcNow > deadline)
                        {
                            try { proc.Kill(); } catch { }
                            return "The installer did not finish in time. Details: " + setupLog;
                        }
                        Thread.Sleep(300);
                    }
                }

                if (InstalledApp.Detect() == null)
                    return "The installer finished, but the app was not found in " + InstalledApp.RootDir + ".";

                // 4. Setup normally starts the app itself; if it did not, start it ourselves.
                var until = DateTime.UtcNow.AddSeconds(10);
                while (!InstalledApp.IsRunning() && DateTime.UtcNow < until) Thread.Sleep(300);
                if (!InstalledApp.IsRunning()) InstalledApp.Launch("--autostart");

                return null;
            }
            catch (Exception ex)
            {
                Log.Write("Install failed: " + ex);
                if (freshInstall && InstalledApp.Detect() == null) RollbackSettings();
                return "Installation failed: " + ex.Message;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void WriteSettings(LauncherSettings s)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisableSteelSeries", s.HideSonarChatAndGaming ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("SettingsConfirmed", 1, RegistryValueKind.DWord);
                // The user already made their choices here, so the app must not treat its first start as a first run.
                key.SetValue("HasRunBefore", 1, RegistryValueKind.DWord);
            }

            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (s.StartWithWindows)
                    run.SetValue(RunValueName, "\"" + InstalledApp.ExePath + "\" --autostart");
                else
                    run.DeleteValue(RunValueName, false);
            }
        }

        // A failed first install must not leave a startup entry pointing at a file that does not exist.
        private static void RollbackSettings()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    run?.DeleteValue(RunValueName, false);
                Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            }
            catch (Exception ex) { Log.Write("Rollback failed: " + ex.Message); }
        }
    }
}
