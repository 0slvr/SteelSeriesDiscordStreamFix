using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Velopack;

namespace SteelSeriesDiscordStreamFix
{
    internal static class Program
    {
        private const string ShowSettingsEventName = "SteelSeriesDiscordStreamFix_ShowSettingsRequest_Event";

        // The installer/launcher sets this to ask a running copy to quit cleanly (un-mute Discord, restore
        // the Sonar devices we hid) before it replaces the files with a newer version.
        private const string ExitRequestEventName = "SteelSeriesDiscordStreamFix_ExitRequest_Event";

        private static DateTime _lastErrorDialogUtc = DateTime.MinValue;

        /// <summary>
        /// Set by MicMuterContext: the same cleanup the normal exit path runs (un-mute Discord, restore the
        /// Sonar devices WE hid). Lets a crash leave Windows in a clean state too.
        /// </summary>
        internal static Action EmergencyCleanup;

        /// <summary>
        /// 0 for a normal start. 1..MaxCrashRestarts when this process was started by the crash handler of the
        /// previous process (argument --crash-restart=N). The app then tells the user it restarted itself.
        /// </summary>
        internal static int CrashRestartCount { get; private set; }

        // A crash loop must never spin forever: after this many quick crashes in a row the app stays down.
        private const int MaxCrashRestarts = 3;
        // A process that ran at least this long before crashing is a new problem, not part of a crash loop.
        private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(10);

        [STAThread]
        static void Main()
        {
            ParseCrashRestartArg();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Log("FATAL UnhandledException: " +
                    ((e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject?.ToString()));
                try
                {
                    var cleanup = EmergencyCleanup;
                    if (cleanup != null) cleanup();
                    else if (AppSettings.WeHidDevices) SteelSeriesToggle.SetChatAndGamingVisible(true);
                }
                catch { }

                // The app has no tray icon, so a crash would silently leave Discord unprotected: start a fresh copy.
                if (e.IsTerminating) TryRestartAfterCrash();
            };
            Application.ThreadException += (s, e) =>
            {
                Logger.Log("FATAL ThreadException: " + e.Exception);
                // Show the dialog at most once per minute so a repeating timer error can't spam the user.
                var nowUtc = DateTime.UtcNow;
                if (nowUtc - _lastErrorDialogUtc < TimeSpan.FromMinutes(1)) return;
                _lastErrorDialogUtc = nowUtc;
                ThemedMessageBox.Show("An unexpected error occurred. Details were written to the log file.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // .NET Framework 4.7.2 can fall back to old protocols on older Windows builds; GitHub needs TLS 1.2+.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(v => UninstallCleanup())
                .Run();

            TryEnableDpiAwareness();

            Logger.LogHeader();
            Logger.Log($"Main() launched. exe={Application.ExecutablePath}");

            bool isAutoStart = Array.Exists(Environment.GetCommandLineArgs(),
                a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));
            if (isAutoStart)
            {
                Logger.Log("Detected --autostart launch (Windows startup) — Settings will not auto-open.");
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var singleInstance = new Mutex(false, "SteelSeriesDiscordStreamFix_SingleInstance_Mutex"))
            {
                bool ownsMutex;
                try
                {
                    ownsMutex = singleInstance.WaitOne(TimeSpan.FromMilliseconds(200));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    // A Windows-startup launch must never pop the Settings window of an instance that already runs.
                    if (!isAutoStart && TrySignalRunningInstance())
                    {
                        return;
                    }

                    Logger.Log("Could not signal a running instance right away — retrying mutex acquisition briefly in case the previous instance is still exiting.");
                    // After a crash the dying process may need a little longer (Windows error reporting) to let go.
                    TimeSpan retryWait = CrashRestartCount > 0 ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(3);
                    try
                    {
                        ownsMutex = singleInstance.WaitOne(retryWait);
                    }
                    catch (AbandonedMutexException)
                    {
                        ownsMutex = true;
                    }

                    if (!ownsMutex)
                    {
                        if (!isAutoStart && !TrySignalRunningInstance())
                        {
                            ThemedMessageBox.Show("SteelSeries Discord Stream Fix is already running in the background.",
                                "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        return;
                    }
                }

                try
                {
                    using (var showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName))
                    using (var exitRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitRequestEventName))
                    {
                        Application.Run(new MicMuterContext(showSettingsEvent, exitRequestEvent, isAutoStart));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("FATAL top-level exception in Main(): " + ex);
                }
                finally
                {
                    try { singleInstance.ReleaseMutex(); } catch { }
                }
            }
        }

        /// <summary>
        /// Runs when Velopack uninstalls the app from ANY place (Windows Settings > Apps, Update.exe, ...),
        /// before the files are removed. The in-app Uninstall button does the same cleanup, but without this
        /// hook a normal Windows uninstall would leave the Sonar Chat/Gaming devices hidden and the
        /// startup entry behind. Velopack exits right after this hook, so keep it quick and never throw.
        /// </summary>
        private static void UninstallCleanup()
        {
            try
            {
                Logger.Log("Velopack uninstall hook: restoring Sonar devices, removing startup entry and settings.");

                // Read this BEFORE the settings key is deleted below.
                if (AppSettings.WeHidDevices)
                {
                    try { SteelSeriesToggle.SetChatAndGamingVisible(true); }
                    catch (Exception ex) { Logger.Log("Uninstall hook: restoring devices failed: " + ex.Message); }
                }

                try { AppSettings.StartWithWindows = false; }
                catch (Exception ex) { Logger.Log("Uninstall hook: removing startup entry failed: " + ex.Message); }

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                        @"SOFTWARE\SteelSeriesDiscordStreamFix", throwOnMissingSubKey: false);
                }
                catch (Exception ex) { Logger.Log("Uninstall hook: registry cleanup failed: " + ex.Message); }

                // Logs live outside the install folder, so remove them here (last, nothing logs after this).
                Logger.DeleteLogFolder();
            }
            catch
            {
                // A hook must never throw.
            }
        }

        private static void ParseCrashRestartArg()
        {
            try
            {
                const string prefix = "--crash-restart=";
                foreach (string a in Environment.GetCommandLineArgs())
                {
                    int n;
                    if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(a.Substring(prefix.Length), out n) && n > 0)
                    {
                        CrashRestartCount = Math.Min(n, MaxCrashRestarts);
                        return;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Called from the crash handler after the cleanup ran: starts a new copy in the background, at most
        /// MaxCrashRestarts times in a row, and only if the user had confirmed their settings.
        /// </summary>
        private static void TryRestartAfterCrash()
        {
            try
            {
                if (!AppSettings.HasConfirmedSettings) return; // never set up (or already uninstalled): nothing to protect

                TimeSpan uptime;
                using (Process me = Process.GetCurrentProcess())
                    uptime = DateTime.Now - me.StartTime;

                int next = uptime >= StableUptime ? 1 : CrashRestartCount + 1;
                if (next > MaxCrashRestarts)
                {
                    Logger.Log("Crash handler: " + MaxCrashRestarts + " quick crashes in a row — NOT restarting again.");
                    return;
                }

                string exe = Application.ExecutablePath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--autostart --crash-restart=" + next,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false
                });
                Logger.Log("Crash handler: started a new copy (attempt " + next + " of " + MaxCrashRestarts + ").");

                // Leave right away, otherwise the Windows "has stopped working" dialog can keep this process (and the
                // single-instance mutex) alive and the new copy would have to wait for it.
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Logger.Log("Crash handler: restart failed: " + ex.Message);
            }
        }

        private static bool TrySignalRunningInstance()
        {
            try
            {
                // This process was just started by the user, so it may bring a window to the front. Let the
                // running instance do it too, otherwise its Settings window can open BEHIND other windows.
                try { AllowSetForegroundWindow(-1 /* ASFW_ANY */); } catch { }

                using (var showEvent = EventWaitHandle.OpenExisting(ShowSettingsEventName))
                {
                    showEvent.Set();
                }
                Logger.Log("Signaled running instance to show Settings.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("TrySignalRunningInstance failed: " + ex.Message);
                return false;
            }
        }

        private static void TryEnableDpiAwareness()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */);
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { /* pre-Vista, irrelevant */ }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }
}
