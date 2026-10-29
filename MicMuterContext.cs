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
        private readonly object _applyLock = new object();
        private DateTime _lastApplyUtc = DateTime.MinValue;

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

            _ = AppUpdater.CheckAndStageAsync(RestartArgs);

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

        private void OnDeviceTopologyChanged()
        {
            // Our own show/hide calls fire device events too. Ignore events that arrive right after
            // we applied the setting, otherwise apply -> event -> apply -> ... could loop forever.
            if (DateTime.UtcNow - _lastApplyUtc < TimeSpan.FromSeconds(6))
            {
                Logger.Log("MicMuterContext: topology event right after our own change — ignored (loop guard).");
                return;
            }
            Logger.Log("MicMuterContext: device topology changed — reapplying SteelSeries chat/gaming visibility.");
            ApplySteelSeriesSetting(AppSettings.SavedDisableSteelSeries);
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
                SteelSeriesToggle.SetChatAndGamingVisible(true);

                _exiting = true;
                try { _showSettingsEvent.Set(); } catch { }

                _watcher.Dispose();
                HideBalloon();

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
                try { SteelSeriesToggle.SetChatAndGamingVisible(!disable); }
                catch (Exception ex) { Logger.Log("ApplySteelSeriesSetting failed: " + ex); }
                _lastApplyUtc = DateTime.UtcNow;
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
            _exiting = true;
            try { _showSettingsEvent.Set(); } catch { }
            _watcher.Dispose();
            // Leave Windows as we found it: bring the Sonar Chat/Gaming devices back on exit.
            // (They are hidden again automatically the next time the app starts.)
            try { SteelSeriesToggle.SetChatAndGamingVisible(true); } catch { }
            HideBalloon();
            Application.Exit();
        }
    }
}