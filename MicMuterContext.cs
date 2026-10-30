using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Velopack.Locators;

namespace SteelSeriesDiscordStreamFix
{
    internal sealed class MicMuterContext : ApplicationContext
    {
        private readonly MuteWatcherService _watcher;
        private readonly EventWaitHandle _showSettingsEvent;
        private readonly SynchronizationContext _uiContext;
        private readonly bool _isAutoStart;
        private Form1 _settingsForm;
        private NotifyIcon _balloon;
        private DateTime _lastBalloonUtc = DateTime.MinValue;
        private bool _watcherStarted;

        private volatile bool _exiting;
        private int _cleanedUp;
        private readonly object _applyLock = new object();
        private readonly System.Collections.Generic.Queue<DateTime> _recentApplies = new System.Collections.Generic.Queue<DateTime>();
        private const int MaxAppliesPerMinute = 5;
        private bool _hideFailureNotified;

        private string[] RestartArgs => _isAutoStart ? new[] { "--autostart" } : Array.Empty<string>();

        public MicMuterContext(EventWaitHandle showSettingsEvent, bool isAutoStart)
        {
            _watcher = new MuteWatcherService();
            _watcher.DeviceTopologyChanged += OnDeviceTopologyChanged;
            _watcher.ProblemDetected += OnWatcherProblem;
            _showSettingsEvent = showSettingsEvent;
            _isAutoStart = isAutoStart;

            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(
                    new WindowsFormsSynchronizationContext());
            }
            _uiContext = SynchronizationContext.Current;

            // A crash also runs the same cleanup (see Program.EmergencyCleanup).
            Program.EmergencyCleanup = Cleanup;

            // Windows logoff/shutdown does not go through ExitApp(), so clean up here as well.
            Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
            Application.ApplicationExit += (s, e) => Cleanup();

            // Updates are applied by closing the app through ExitApp() so Cleanup() always runs first.
            AppUpdater.RequestExit = () => _uiContext.Post(_ => ExitApp(), null);
            _ = AppUpdater.RunPeriodicChecksAsync(() => _exiting, RestartArgs, isAutoStart);

            ApplySteelSeriesSetting(AppSettings.SavedDisableSteelSeries);

            StartShowSettingsListener();

            bool showSettingsOnLaunch = true;

            if (AppSettings.IsFirstRun)
            {
                Logger.Log("First run detected — showing settings window; mute watcher will start once OK/Save is pressed.");
                AppSettings.MarkFirstRunComplete();
            }
            else
            {
                EnsureWatcherStarted();

                if (isAutoStart)
                {
                    showSettingsOnLaunch = false;
                    Logger.Log("Launched via Windows startup — running in the background, not opening Settings.");
                }
            }

            if (showSettingsOnLaunch)
            {
                ShowSettings();
            }
        }

        private void EnsureWatcherStarted()
        {
            if (_watcherStarted) return;
            _watcherStarted = true;
            _watcher.Start();
            Logger.Log("MicMuterContext: mute watcher started, running in background, no tray icon.");
        }

        // Problems are shown as a short-lived balloon notification (no permanent tray icon).
        private void OnWatcherProblem(string message)
        {
            if (_exiting) return;
            _uiContext.Post(_ => ShowBalloon(message), null);
        }

        private void ShowBalloon(string message)
        {
            if (_exiting) return;
            if (DateTime.UtcNow - _lastBalloonUtc < TimeSpan.FromSeconds(60)) return;
            _lastBalloonUtc = DateTime.UtcNow;

            try
            {
                HideBalloon();

                _balloon = new NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Warning,
                    Visible = true,
                    BalloonTipTitle = "SteelSeries Discord Stream Fix",
                    BalloonTipText = message,
                    BalloonTipIcon = ToolTipIcon.Warning
                };
                _balloon.BalloonTipClicked += (s, e) => { HideBalloon(); ShowSettings(); };
                _balloon.BalloonTipClosed += (s, e) => HideBalloon();
                _balloon.ShowBalloonTip(10000);

                var hideTimer = new System.Windows.Forms.Timer { Interval = 15000 };
                hideTimer.Tick += (s, e) =>
                {
                    hideTimer.Stop();
                    hideTimer.Dispose();
                    HideBalloon();
                };
                hideTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("ShowBalloon failed: " + ex);
            }
        }

        private void HideBalloon()
        {
            if (_balloon == null) return;
            try
            {
                _balloon.Visible = false;
                _balloon.Dispose();
            }
            catch { }
            _balloon = null;
        }

        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            Logger.Log("MicMuterContext: Windows session ending (" + e.Reason + ") — restoring devices and un-muting Discord.");
            Cleanup();
        }

        private void OnDeviceTopologyChanged()
        {
            if (_exiting) return;

            bool disable = AppSettings.SavedDisableSteelSeries;

            // Option off: we only ever restore devices WE hid. If we did not hide any, there is nothing to do
            // (the user may have hidden them some other way on purpose).
            if (!disable && !AppSettings.WeHidDevices) return;

            // Our own hide/show calls make Windows raise device notifications. Only act when the devices are
            // really not in the wanted state; after our change they are, so the chain stops by itself.
            if (!SteelSeriesToggle.NeedsChange(!disable))
            {
                Logger.Log("MicMuterContext: topology event — Sonar Chat/Gaming already in the wanted state, nothing to do.");
                return;
            }

            // Safety net on top of the state check: never more than a few applies per minute.
            lock (_applyLock)
            {
                var now = DateTime.UtcNow;
                while (_recentApplies.Count > 0 && now - _recentApplies.Peek() > TimeSpan.FromMinutes(1))
                    _recentApplies.Dequeue();
                if (_recentApplies.Count >= MaxAppliesPerMinute)
                {
                    Logger.Log("MicMuterContext: too many device changes in a minute — skipping (loop protection).");
                    return;
                }
            }

            Logger.Log("MicMuterContext: device topology changed — reapplying SteelSeries chat/gaming visibility.");
            ApplySteelSeriesSetting(disable);
        }

        private void StartShowSettingsListener()
        {
            var thread = new Thread(() =>
            {
                while (!_exiting)
                {
                    if (_showSettingsEvent.WaitOne())
                    {
                        if (_exiting) break;

                        try
                        {
                            _uiContext.Post(_ => ShowSettings(), null);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("StartShowSettingsListener: failed to post ShowSettings to UI thread: " + ex);
                        }
                    }
                }
            })
            {
                IsBackground = true,
                Name = "ShowSettingsListener"
            };
            thread.Start();
        }

        private void UninstallApp()
        {
            var confirm = MessageBox.Show(
                "This will restore SteelSeries devices, disable auto-start, and completely remove " +
                "SteelSeries Discord Stream Fix — including all installed files, shortcuts, and settings. Continue?",
                "Uninstall SteelSeries Discord Stream Fix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                Cleanup();

                AppSettings.StartWithWindows = false;

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                        @"SOFTWARE\SteelSeriesDiscordStreamFix", throwOnMissingSubKey: false);
                }
                catch (Exception ex) { Logger.Log("Registry cleanup failed: " + ex); }

                Logger.Log("UninstallApp: our own settings cleared, SteelSeries devices restored, auto-start disabled.");

                string updateExePath = null;
                try
                {
                    updateExePath = VelopackLocator.Current?.UpdateExePath;
                }
                catch (Exception ex)
                {
                    Logger.Log("UninstallApp: could not resolve Update.exe path: " + ex);
                }

                if (!string.IsNullOrEmpty(updateExePath) && File.Exists(updateExePath))
                {
                    Logger.Log($"UninstallApp: launching Velopack uninstaller at '{updateExePath}' to remove all installed files.");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = updateExePath,
                        Arguments = "uninstall --silent",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    Application.Exit();
                }
                else
                {
                    Logger.Log("UninstallApp: Update.exe not found — likely running from source/dev; no install files to remove.");

                    MessageBox.Show(
                        "Settings removed and SteelSeries devices restored. This copy isn't running from an " +
                        "installed location, so there are no installed app files to remove automatically — " +
                        "delete the folder manually if needed.",
                        "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("UninstallApp failed: " + ex);
                MessageBox.Show("Uninstall did not complete fully. See the log file for details.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private void ApplySteelSeriesSetting(bool disable)
        {
            // Serialized: the UI thread and the topology timer thread must never run this at the same time.
            lock (_applyLock)
            {
                // Option off: only bring devices back if WE hid them. Never force-show devices the user
                // hid some other way.
                if (!disable && !AppSettings.WeHidDevices) return;

                _recentApplies.Enqueue(DateTime.UtcNow);
                bool ok = false;
                try { ok = SteelSeriesToggle.SetChatAndGamingVisible(!disable); }
                catch (Exception ex) { Logger.Log("ApplySteelSeriesSetting failed: " + ex); }

                if (ok)
                {
                    AppSettings.WeHidDevices = disable;
                    _hideFailureNotified = false;
                }
                else if (!_hideFailureNotified)
                {
                    _hideFailureNotified = true;
                    OnWatcherProblem("Could not " + (disable ? "hide" : "show") +
                        " the SteelSeries Chat/Gaming devices. A Windows update may have changed the undocumented API this relies on. See the log file.");
                }
            }
        }

        private void ShowSettings()
        {
            if (_exiting) return;

            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new Form1(ApplySteelSeriesSetting, ExitApp, UninstallApp, EnsureWatcherStarted, RestartArgs, () => _watcher.StatusText);
            }
            _settingsForm.Show();
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void ExitApp()
        {
            Cleanup();
            Application.Exit();
        }

        /// <summary>
        /// Leaves Windows as we found it: un-mutes Discord and brings back the Sonar Chat/Gaming devices we hid.
        /// Safe to call several times and from any exit path (Exit, logoff, shutdown, uninstall).
        /// </summary>
        private void Cleanup()
        {
            if (System.Threading.Interlocked.Exchange(ref _cleanedUp, 1) == 1) return;

            _exiting = true;
            try { Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
            try { _showSettingsEvent.Set(); } catch { }
            try { _watcher.Dispose(); } catch (Exception ex) { Logger.Log("Cleanup: watcher dispose failed: " + ex.Message); }

            // Restore only what we hid. (They are hidden again automatically the next time the app starts.)
            if (AppSettings.WeHidDevices)
            {
                try
                {
                    if (SteelSeriesToggle.SetChatAndGamingVisible(true))
                        AppSettings.WeHidDevices = false;
                }
                catch (Exception ex) { Logger.Log("Cleanup: restoring devices failed: " + ex.Message); }
            }
            try { HideBalloon(); } catch { }
        }
    }
}
