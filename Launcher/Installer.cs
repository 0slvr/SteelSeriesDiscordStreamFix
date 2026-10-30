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
        public static string Install(LauncherSettings settings, Action<string> progress = null)
        {
            Log.Write("Install started. fresh=" + (settings != null));
            string tempDir = Path.Combine(Path.GetTempPath(), "SteelSeriesDiscordStreamFix-setup-" + Guid.NewGuid().ToString("N"));
            bool freshInstall = settings != null;

            try
            {
                Directory.CreateDirectory(tempDir);
                if (progress != null) progress("Preparing the installer...");
                string setupPath = Path.Combine(tempDir, "SteelSeriesDiscordStreamFix-win-Setup.exe");
                using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("Setup.exe"))
                {
                    if (res == null) return "This build does not contain the installer (development build).";
                    using (var file = File.Create(setupPath)) res.CopyTo(file);
                }

                Log.Write("Extracted Setup to " + setupPath);

                // 1. Save the options first so the app already has them when it starts for the first time.
                if (settings != null) WriteSettings(settings);

                Log.Write("Settings written.");

                // 2. Close a running copy cleanly (it restores the devices it hid and un-mutes Discord).
                if (progress != null) progress("Closing the running copy...");
                InstalledApp.RequestExitAndWait(8000);
                Log.Write("Running copy handled.");

                // 3. Velopack's own Setup, silent. It starts the app without arguments, so a one-time registry flag
                //    tells the app to start quietly (no Settings window) this once.
                SetQuietFlag(true);
                Directory.CreateDirectory(Log.Dir);
                string setupLog = Path.Combine(Log.Dir, "setup-log.txt");
                var psi = new ProcessStartInfo
                {
                    FileName = setupPath,
                    // NOTE: do not pass app arguments after "--": Velopack's Setup panics on them (clap EXE_ARGS bug) and hangs.
                    Arguments = "--silent --log \"" + setupLog + "\"",
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Log.Write("Running embedded Setup: " + psi.Arguments);
                if (progress != null) progress("Installing... (this can take up to a minute)");
                Version mine = Assembly.GetExecutingAssembly().GetName().Version;
                var target = new Version(mine.Major, mine.Minor, mine.Build);

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return "The installer could not be started.";

                    // Normally Setup exits after it started the app. Do not hang if it stays open: once the new
                    // version is installed AND running, the job is done.
                    Log.Write("Setup started, pid " + proc.Id);
                    var deadline = DateTime.UtcNow.AddMinutes(2);
                    DateTime? runningSince = null;
                    var nextBeat = DateTime.UtcNow.AddSeconds(5);
                    while (true)
                    {
                        if (DateTime.UtcNow >= nextBeat)
                        {
                            nextBeat = DateTime.UtcNow.AddSeconds(5);
                            InstalledApp d = InstalledApp.Detect();
                            Log.Write("Waiting: setupExited=" + proc.HasExited + ", installed=" + (d == null ? "none" : d.Version.ToString()) +
                                      ", appRunning=" + InstalledApp.IsRunning());
                        }

                        if (proc.HasExited)
                        {
                            Log.Write("Setup exit code: " + proc.ExitCode);
                            if (proc.ExitCode != 0)
                                return "The installer failed (exit code " + proc.ExitCode + "). Details: " + setupLog;
                            break;
                        }

                        // Velopack briefly runs the app for its install hook, so "running" only counts when it stays up.
                        if (InstalledApp.IsRunning()) { if (runningSince == null) runningSince = DateTime.UtcNow; }
                        else runningSince = null;

                        InstalledApp now = InstalledApp.Detect();
                        if (now != null && now.Version >= target && runningSince != null &&
                            DateTime.UtcNow - runningSince.Value > TimeSpan.FromSeconds(2))
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
                // Setup has exited, so its install hook is done: a short grace period is enough for it to have started the app.
                if (!WaitForStableRunning(3000))
                {
                    SetQuietFlag(false);
                    InstalledApp.Launch("--autostart");
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Write("Install failed: " + ex);
                SetQuietFlag(false);
                if (freshInstall && InstalledApp.Detect() == null) RollbackSettings();
                return "Installation failed: " + ex.Message;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static bool WaitForStableRunning(int timeoutMs)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            DateTime? since = null;
            while (DateTime.UtcNow < until)
            {
                if (InstalledApp.IsRunning())
                {
                    if (since == null) since = DateTime.UtcNow;
                    else if (DateTime.UtcNow - since.Value > TimeSpan.FromSeconds(1)) return true;
                }
                else since = null;
                Thread.Sleep(250);
            }
            return false;
        }

        private static void SetQuietFlag(bool on)
        {
            try
            {
                if (on)
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                        key.SetValue("QuietStartOnce", DateTime.UtcNow.Ticks.ToString(), RegistryValueKind.String);
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
                        key?.DeleteValue("QuietStartOnce", false);
                }
            }
            catch (Exception ex) { Log.Write("SetQuietFlag failed: " + ex.Message); }
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
