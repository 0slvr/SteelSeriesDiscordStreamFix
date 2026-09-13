using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    internal static class Program
    {
        // Second launches signal this event to ask the already-running
        // instance to pop its settings window back up. This is the ONLY
        // way back into Settings/Restore/Uninstall/Exit now that there's
        // no tray icon or menu — just double-click the exe again.
        private const string ShowSettingsEventName = "SteelSeriesDiscordStreamFix_ShowSettingsRequest_Event";

        [STAThread]
        static void Main()
        {
            // Register global handlers FIRST so any unhandled exception gets
            // logged instead of silently killing the process.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Log("FATAL UnhandledException: " + (e.ExceptionObject as Exception)?.ToString()
                    ?? e.ExceptionObject?.ToString());
            };
            Application.ThreadException += (s, e) =>
            {
                Logger.Log("FATAL ThreadException: " + e.Exception);
                MessageBox.Show("An unexpected error occurred. Details were written to the log file.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            TryEnableDpiAwareness();

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
                    if (TrySignalRunningInstance())
                    {
                        return;
                    }

                    Logger.Log("Could not signal a running instance right away — retrying mutex acquisition briefly in case the previous instance is still exiting.");
                    try
                    {
                        ownsMutex = singleInstance.WaitOne(TimeSpan.FromSeconds(3));
                    }
                    catch (AbandonedMutexException)
                    {
                        ownsMutex = true;
                    }

                    if (!ownsMutex)
                    {
                        if (!TrySignalRunningInstance())
                        {
                            MessageBox.Show("SteelSeries Discord Stream Fix is already running in the background.",
                                "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        return;
                    }
                }

                try
                {
                    using (var showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName))
                    {
                        Application.Run(new MicMuterContext(showSettingsEvent, isAutoStart));
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

        private static bool TrySignalRunningInstance()
        {
            try
            {
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }

    internal static class Logger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteelSeriesDiscordStreamFix");

        private static readonly string LogPath = Path.Combine(LogDir, "log.txt");
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
                    File.Delete(LogPath);
                }
            }
            catch
            {
                // If the check/delete fails, just keep appending.
            }
        }
    }

    public static class AppSettings
    {
        private const string RegistryPath = @"SOFTWARE\SteelSeriesDiscordStreamFix";
        private const string ValueNameDisableSteelSeries = "DisableSteelSeries";

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SteelSeriesDiscordStreamFix";

        public static bool SavedDisableSteelSeries
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    {
                        if (key == null) return false;
                        object value = key.GetValue(ValueNameDisableSteelSeries);
                        return value != null && Convert.ToInt32(value) != 0;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("SavedDisableSteelSeries read failed: " + ex.Message);
                    return false;
                }
            }
        }

        public static void SaveConfig(bool disableSteelSeries)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue(ValueNameDisableSteelSeries, disableSteelSeries ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("SaveConfig failed: " + ex.Message);
            }
        }

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                        return key?.GetValue(RunValueName) != null;
                }
                catch (Exception ex)
                {
                    Logger.Log("StartWithWindows read failed: " + ex.Message);
                    return false;
                }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        if (value)
                            key?.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --autostart");
                        else
                            key?.DeleteValue(RunValueName, false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("StartWithWindows write failed: " + ex.Message);
                }
            }
        }
        private const string ValueNameHasRunBefore = "HasRunBefore";

        public static bool IsFirstRun
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    {
                        if (key == null) return true;
                        return key.GetValue(ValueNameHasRunBefore) == null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("IsFirstRun read failed: " + ex.Message);
                    return false;
                }
            }
        }

        public static void MarkFirstRunComplete()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue(ValueNameHasRunBefore, 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("MarkFirstRunComplete failed: " + ex.Message);
            }
        }

        private const string ValueNameSettingsConfirmed = "SettingsConfirmed";

        public static bool HasConfirmedSettings
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    {
                        if (key == null) return false;
                        object value = key.GetValue(ValueNameSettingsConfirmed);
                        return value != null && Convert.ToInt32(value) != 0;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("HasConfirmedSettings read failed: " + ex.Message);
                    return false;
                }
            }
        }

        public static void MarkSettingsConfirmed()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue(ValueNameSettingsConfirmed, 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("MarkSettingsConfirmed failed: " + ex.Message);
            }
        }
    }

    internal sealed class MuteWatcherService : IMMNotificationClient, IDisposable
    {
        public const string MicEndpointNameFragment = "SteelSeries Sonar - Microphone";

        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly object _lock = new object();
        private readonly Dictionary<int, AudioSessionControl> _trackedSessions = new Dictionary<int, AudioSessionControl>();
        private readonly Dictionary<int, DiscordSessionEventsHandler> _sessionEventHandlers = new Dictionary<int, DiscordSessionEventsHandler>();

        private MMDevice _targetDevice;
        private AudioSessionManager _sessionManager;
        private System.Windows.Forms.Timer _safetyNetTimer;
        private bool _disposed;

        public void Start()
        {
            _enumerator.RegisterEndpointNotificationCallback(this);

            try
            {
                BindToTargetDevice();
            }
            catch (Exception ex)
            {
                Logger.Log("Start: initial BindToTargetDevice failed, will retry via safety net/device events: " + ex);
            }

            _safetyNetTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
            _safetyNetTimer.Tick += (s, e) => SafetyNetCheck();
            _safetyNetTimer.Start();

            Logger.Log("MuteWatcherService started (event-driven, 5-minute safety net).");
        }

        private void BindToTargetDevice()
        {
            lock (_lock)
            {
                UnbindCurrentDevice();

                MMDevice found;
                try
                {
                    found = FindTargetDevice();
                }
                catch (Exception ex)
                {
                    Logger.Log("BindToTargetDevice: FindTargetDevice failed: " + ex);
                    found = null;
                }

                if (found == null)
                {
                    Logger.Log("MuteWatcherService: target microphone endpoint not found (will retry on next device-change event or safety-net tick).");
                    return;
                }

                _targetDevice = found;
                Logger.Log($"MuteWatcherService: bound to '{_targetDevice.FriendlyName}' (id={_targetDevice.ID})");

                try
                {
                    _sessionManager = _targetDevice.AudioSessionManager;
                    _sessionManager.OnSessionCreated += OnSessionCreated;

                    SessionCollection sessions = _sessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        TrackIfDiscord(sessions[i]);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("BindToTargetDevice: session manager setup failed: " + ex);
                }
            }
        }

        private MMDevice FindTargetDevice()
        {
            foreach (MMDevice device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (device.FriendlyName.IndexOf(MicEndpointNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return device;
                device.Dispose();
            }
            return null;
        }

        private void OnSessionCreated(object sender, IAudioSessionControl newSession)
        {
            try
            {
                var session = new AudioSessionControl(newSession);
                TrackIfDiscord(session);
            }
            catch (Exception ex)
            {
                Logger.Log("OnSessionCreated failed: " + ex.Message);
            }
        }

        private void TrackIfDiscord(AudioSessionControl session)
        {
            int pid;
            try { pid = (int)session.GetProcessID; }
            catch { return; }

            if (pid == 0) return;

            string processName;
            try
            {
                using (Process proc = Process.GetProcessById(pid))
                    processName = proc.ProcessName;
            }
            catch
            {
                return;
            }

            if (processName.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) < 0) return;

            lock (_lock)
            {
                if (_trackedSessions.ContainsKey(pid)) return;
                _trackedSessions[pid] = session;
            }

            MuteSession(session, $"new Discord session detected (pid={pid})");

            var handler = new DiscordSessionEventsHandler(pid, this, session);
            session.RegisterEventClient(handler);
            lock (_lock) { _sessionEventHandlers[pid] = handler; }
        }

        private void MuteSession(AudioSessionControl session, string reason)
        {
            try
            {
                if (!session.SimpleAudioVolume.Mute)
                {
                    session.SimpleAudioVolume.Mute = true;
                    Logger.Log("MuteWatcherService: muted Discord — " + reason);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("MuteSession failed: " + ex.Message);
            }
        }

        private void UnbindCurrentDevice()
        {
            if (_sessionManager != null)
            {
                try { _sessionManager.OnSessionCreated -= OnSessionCreated; } catch { }
                _sessionManager = null;
            }

            foreach (var kv in _trackedSessions)
            {
                if (_sessionEventHandlers.TryGetValue(kv.Key, out var h))
                {
                    try { kv.Value.UnRegisterEventClient(h); } catch { }
                }
            }
            _sessionEventHandlers.Clear();
            _trackedSessions.Clear();
            _targetDevice?.Dispose();
            _targetDevice = null;
        }

        private void SafetyNetCheck()
        {
            lock (_lock)
            {
                bool stillValid = _targetDevice != null && DeviceStillExists(_targetDevice.ID);
                if (!stillValid)
                {
                    Logger.Log("SafetyNetCheck: target device missing or changed — rebinding.");
                    try
                    {
                        BindToTargetDevice();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("SafetyNetCheck: rebind failed, will retry next tick: " + ex);
                    }
                }
            }
        }

        private bool DeviceStillExists(string deviceId)
        {
            try
            {
                using (MMDevice d = _enumerator.GetDevice(deviceId))
                    return d.State == DeviceState.Active;
            }
            catch
            {
                return false;
            }
        }

        public void OnDeviceAdded(string deviceId) => ScheduleTopologyRecheck();
        public void OnDeviceRemoved(string deviceId) => ScheduleTopologyRecheck();
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => ScheduleTopologyRecheck();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void ScheduleTopologyRecheck()
        {
            if (_disposed) return;
            Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        bool currentStillValid = _targetDevice != null && DeviceStillExists(_targetDevice.ID);
                        if (!currentStillValid)
                            BindToTargetDevice();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ScheduleTopologyRecheck failed: " + ex);
                }
            });
        }

        public void Dispose()
        {
            _disposed = true;
            _safetyNetTimer?.Stop();
            _safetyNetTimer?.Dispose();
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            UnbindCurrentDevice();
            _enumerator.Dispose();
        }

        private sealed class DiscordSessionEventsHandler : IAudioSessionEventsHandler
        {
            private readonly int _pid;
            private readonly MuteWatcherService _owner;
            private readonly AudioSessionControl _session;

            public DiscordSessionEventsHandler(int pid, MuteWatcherService owner, AudioSessionControl session)
            {
                _pid = pid;
                _owner = owner;
                _session = session;
            }

            public void OnVolumeChanged(float volume, bool isMuted)
            {
                if (!isMuted)
                    _owner.MuteSession(_session, "session was un-muted externally — re-muting");
            }

            public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
            {
                lock (_owner._lock)
                {
                    _owner._trackedSessions.Remove(_pid);
                    _owner._sessionEventHandlers.Remove(_pid);
                }
                Logger.Log($"Discord session (pid={_pid}) disconnected: {disconnectReason}");
            }

            public void OnDisplayNameChanged(string displayName) { }
            public void OnIconPathChanged(string iconPath) { }
            public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
            public void OnGroupingParamChanged(ref Guid groupingId) { }
            public void OnStateChanged(AudioSessionState state) { }
        }
    }

    internal sealed class MicMuterContext : ApplicationContext
    {
        private readonly MuteWatcherService _watcher;
        private readonly EventWaitHandle _showSettingsEvent;
        private readonly SynchronizationContext _uiContext;
        private Form1 _settingsForm;
        private bool _watcherStarted;

        // Transient tray icon used ONLY to show the update balloon-tip
        // "toast". It appears when an update is found and disappears again
        // once the balloon closes — the app still has no persistent tray
        // icon or menu the rest of the time.
        private NotifyIcon _updateNotifyIcon;
        private UpdateInfo _toastUpdateInfo;

        private volatile bool _exiting;

        public MicMuterContext(EventWaitHandle showSettingsEvent, bool isAutoStart)
        {
            _watcher = new MuteWatcherService();
            _showSettingsEvent = showSettingsEvent;

            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Forms.WindowsFormsSynchronizationContext());
            }
            _uiContext = SynchronizationContext.Current;

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

            // Quiet background update check. Never shows any UI on its
            // own — see UpdateChecker's class comment. Started on every
            // launch, including autostart, since a background check
            // doesn't display anything by itself.
            UpdateChecker.OnUpdateAvailable += info => _uiContext.Post(_ => ShowUpdateToast(info), null);
            UpdateChecker.Start();
        }

        private void EnsureWatcherStarted()
        {
            if (_watcherStarted) return;
            _watcherStarted = true;
            _watcher.Start();
            Logger.Log("MicMuterContext: mute watcher started, running in background, no tray icon.");
        }

        /// <summary>
        /// Pops a Windows balloon-tip "toast" near the system tray telling
        /// the user an update is ready, without requiring them to open
        /// Settings first. Clicking the balloon starts the same confirm-then-
        /// download flow as the link inside Settings. The icon used for the
        /// balloon is removed again once it closes (clicked, dismissed, or
        /// timed out) so the app doesn't gain a permanent tray presence.
        /// </summary>
        private void ShowUpdateToast(UpdateInfo info)
        {
            try
            {
                _toastUpdateInfo = info;

                _updateNotifyIcon?.Dispose();
                _updateNotifyIcon = new NotifyIcon
                {
                    Icon = SafeExtractIcon(),
                    Visible = true,
                    BalloonTipTitle = "SteelSeries Discord Stream Fix — تحديث متوفر",
                    BalloonTipText = $"يتوفر إصدار جديد v{info.Version}. اضغط هنا للتحديث الآن.",
                    BalloonTipIcon = ToolTipIcon.Info
                };
                _updateNotifyIcon.BalloonTipClicked += async (s, e) => await OnUpdateToastClicked();
                _updateNotifyIcon.BalloonTipClosed += (s, e) => RemoveUpdateToastIcon();
                _updateNotifyIcon.Click += (s, e) => { }; // left-click on the transient icon itself does nothing extra

                _updateNotifyIcon.ShowBalloonTip(10000);
                Logger.Log($"MicMuterContext: showed update toast for v{info.Version}.");
            }
            catch (Exception ex)
            {
                Logger.Log("ShowUpdateToast failed: " + ex);
            }
        }

        private static Icon SafeExtractIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void RemoveUpdateToastIcon()
        {
            try
            {
                if (_updateNotifyIcon != null)
                {
                    _updateNotifyIcon.Visible = false;
                    _updateNotifyIcon.Dispose();
                    _updateNotifyIcon = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("RemoveUpdateToastIcon failed: " + ex);
            }
        }

        private async Task OnUpdateToastClicked()
        {
            var info = _toastUpdateInfo ?? UpdateChecker.PendingUpdate;
            RemoveUpdateToastIcon();
            if (info == null) return;

            string notesPart = string.IsNullOrWhiteSpace(info.Notes) ? "" : "\n\n" + info.Notes;
            var confirm = MessageBox.Show(
                $"يتوفر إصدار جديد (v{info.Version}). سيتم إغلاق التطبيق وإعادة فتحه تلقائياً بعد التحديث.{notesPart}\n\nهل تريد التحديث الآن؟",
                "تحديث SteelSeries Discord Stream Fix",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            await UpdateChecker.DownloadAndApplyAsync(info, err =>
            {
                _uiContext.Post(_ =>
                {
                    MessageBox.Show("تعذر تنزيل التحديث. حاول مرة أخرى لاحقاً أو راجع ملف السجل.",
                        "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }, null);
            });
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
                "This will remove all settings, restore SteelSeries devices, and disable auto-start. " +
                "The app files themselves will NOT be deleted — you can remove the exe manually afterward. Continue?",
                "Uninstall SteelSeries Discord Stream Fix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                SteelSeriesToggle.SetChatAndGamingVisible(true);

                _exiting = true;
                try { _showSettingsEvent.Set(); } catch { }

                RemoveUpdateToastIcon();
                _watcher.Dispose();

                AppSettings.StartWithWindows = false;

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                        @"SOFTWARE\SteelSeriesDiscordStreamFix", throwOnMissingSubKey: false);
                }
                catch (Exception ex) { Logger.Log("Registry cleanup failed: " + ex); }

                Logger.Log("UninstallApp: settings cleared, SteelSeries devices restored, auto-start disabled. App files left in place.");

                MessageBox.Show(
                    "Settings removed and SteelSeries devices restored. You can now close the app; " +
                    "delete the exe manually if you want to remove it completely.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Application.Exit();
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
            try { SteelSeriesToggle.SetChatAndGamingVisible(!disable); }
            catch (Exception ex) { Logger.Log("ApplySteelSeriesSetting failed: " + ex); }
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new Form1(ApplySteelSeriesSetting, ExitApp, UninstallApp, EnsureWatcherStarted);
            }
            _settingsForm.RefreshUpdateStatus();
            _settingsForm.Show();
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void ExitApp()
        {
            _exiting = true;
            try { _showSettingsEvent.Set(); } catch { } // wake the listener thread so it can exit
            RemoveUpdateToastIcon();
            _watcher.Dispose();
            Application.Exit();
        }
    }

    public partial class Form1 : Form
    {
        // Modern dark palette
        private static readonly Color ColorBackground = Color.FromArgb(22, 22, 29);
        private static readonly Color ColorAccent = Color.FromArgb(94, 106, 246);
        private static readonly Color ColorAccentHover = Color.FromArgb(112, 123, 250);
        private static readonly Color ColorTextPrimary = Color.FromArgb(240, 240, 244);
        private static readonly Color ColorTextSecondary = Color.FromArgb(150, 150, 164);
        private static readonly Color ColorSurface = Color.FromArgb(38, 38, 48);
        private static readonly Color ColorSurfaceHover = Color.FromArgb(48, 48, 60);
        private static readonly Color ColorBorder = Color.FromArgb(58, 58, 72);
        private static readonly Color ColorDanger = Color.FromArgb(220, 96, 96);

        private Label labelTitle;
        private Label labelInstructions;
        private LinkLabel linkUpdate;
        private ModernCheckBox chkDisable;
        private ModernCheckBox chkStartWithWindows;
        private ModernButton btnOk;
        private ModernButton btnCancel;
        private LinkLabel linkRestore;
        private LinkLabel linkUninstall;

        private readonly Action<bool> _applySteelSeriesSetting;
        private readonly Action _exitApp;
        private readonly Action _uninstallApp;
        private readonly Action _ensureWatcherStarted;

        public Form1(Action<bool> applySteelSeriesSetting, Action exitApp, Action uninstallApp, Action ensureWatcherStarted)
        {
            _applySteelSeriesSetting = applySteelSeriesSetting;
            _exitApp = exitApp;
            _uninstallApp = uninstallApp;
            _ensureWatcherStarted = ensureWatcherStarted;
            InitializeComponent();
            InitializeCustomComponents();
            LoadCurrentSettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableDarkTitleBar();
        }

        private void TryEnableDarkTitleBar()
        {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(460, 372);
            Name = "Form1";
            Text = "SteelSeries Discord Stream Fix — Settings";
            ResumeLayout(false);
        }

        private void InitializeCustomComponents()
        {
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);
            ShowInTaskbar = true;

            labelTitle = new Label
            {
                AutoSize = false,
                Location = new Point(32, 26),
                Size = new Size(396, 28),
                Text = "SteelSeries Discord Stream Fix",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            labelInstructions = new Label
            {
                AutoSize = false,
                Location = new Point(32, 66),
                Size = new Size(396, 72),
                Text = "Running in the background: Discord is auto-muted the instant it opens a " +
                       $"session on \"{MuteWatcherService.MicEndpointNameFragment}\", and re-muted instantly " +
                       "if anything unmutes it. There's no tray icon — reopen this window any time by " +
                       "running the app again.",
                ForeColor = ColorTextSecondary,
                Font = new Font("Segoe UI", 9.5F),
                BackColor = Color.Transparent
            };

            // Hidden until RefreshUpdateStatus() (called every time this
            // window is (re)shown) finds a pending update from the quiet
            // background UpdateChecker. A Windows toast also pops
            // independently the moment an update is first found — see
            // MicMuterContext.ShowUpdateToast — so the user doesn't have to
            // open this window at all to be notified.
            linkUpdate = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(32, 142),
                Text = "",
                Visible = false,
                LinkColor = ColorAccent,
                ActiveLinkColor = ColorAccentHover,
                VisitedLinkColor = ColorAccent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            linkUpdate.LinkClicked += async (s, e) => await OnUpdateClicked();

            chkDisable = new ModernCheckBox
            {
                Location = new Point(32, 174),
                Size = new Size(396, 24),
                Text = "Also hide SteelSeries Chat & Gaming devices",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F)
            };

            chkStartWithWindows = new ModernCheckBox
            {
                Location = new Point(32, 206),
                Size = new Size(396, 24),
                Text = "Start automatically when I log in",
                AccentColor = ColorAccent,
                BorderColor = ColorBorder,
                TextColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 9.5F)
            };

            linkRestore = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(32, 248),
                Text = "Restore devices",
                LinkColor = ColorTextSecondary,
                ActiveLinkColor = ColorTextPrimary,
                VisitedLinkColor = ColorTextSecondary,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F)
            };
            linkRestore.LinkClicked += (s, e) => BtnRestore_Click(s, e);

            linkUninstall = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(150, 248),
                Text = "Uninstall...",
                LinkColor = ColorDanger,
                ActiveLinkColor = ColorDanger,
                VisitedLinkColor = ColorDanger,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F)
            };
            linkUninstall.LinkClicked += (s, e) => _uninstallApp();

            btnCancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(90, 36),
                Location = new Point(228, 312),
                BaseColor = ColorSurface,
                HoverColor = ColorSurfaceHover,
                TextColor = ColorTextPrimary,
                BorderColor = ColorBorder
            };
            btnCancel.Click += (s, e) => CloseOrHide();

            btnOk = new ModernButton
            {
                Text = "Save",
                Size = new Size(108, 36),
                Location = new Point(324, 312),
                BaseColor = ColorAccent,
                HoverColor = ColorAccentHover,
                TextColor = Color.White
            };
            btnOk.Click += BtnOk_Click;

            Controls.Add(labelTitle);
            Controls.Add(labelInstructions);
            Controls.Add(linkUpdate);
            Controls.Add(chkDisable);
            Controls.Add(chkStartWithWindows);
            Controls.Add(linkRestore);
            Controls.Add(linkUninstall);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) CloseOrHide(); };
        }

        /// <summary>
        /// Called every time Settings is (re)shown. Shows or hides the
        /// small update link depending on whether the quiet background
        /// UpdateChecker has found a newer version. Never pops anything up
        /// on its own — the link only ever appears inside a window the
        /// user opened themselves. (The Windows toast in MicMuterContext
        /// covers the "user never opens Settings" case separately.)
        /// </summary>
        public void RefreshUpdateStatus()
        {
            var info = UpdateChecker.PendingUpdate;
            if (info != null)
            {
                linkUpdate.Text = $"Update available: v{info.Version} — Update now";
                linkUpdate.Visible = true;
            }
            else
            {
                linkUpdate.Visible = false;
            }
        }

        private async Task OnUpdateClicked()
        {
            var info = UpdateChecker.PendingUpdate;
            if (info == null) return;

            string notesPart = string.IsNullOrWhiteSpace(info.Notes) ? "" : "\n\n" + info.Notes;
            var confirm = MessageBox.Show(
                $"A newer version is available (v{info.Version}). The app will close and reopen itself after updating.{notesPart}\n\nUpdate now?",
                "Update SteelSeries Discord Stream Fix",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            linkUpdate.Enabled = false;
            linkUpdate.Text = "Downloading update...";

            await UpdateChecker.DownloadAndApplyAsync(info, err =>
            {
                _uiContextInvoke(() =>
                {
                    linkUpdate.Enabled = true;
                    MessageBox.Show("The update could not be downloaded. Try again later or check the log file.",
                        "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RefreshUpdateStatus();
                });
            });
        }

        private void _uiContextInvoke(Action a)
        {
            if (InvokeRequired) Invoke(a); else a();
        }

        private void CloseOrHide()
        {
            if (AppSettings.HasConfirmedSettings)
            {
                Hide();
            }
            else
            {
                _exitApp();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                CloseOrHide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void LoadCurrentSettings()
        {
            chkDisable.Checked = AppSettings.SavedDisableSteelSeries;
            chkStartWithWindows.Checked = AppSettings.StartWithWindows;
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            try
            {
                SteelSeriesToggle.SetChatAndGamingVisible(true);
                AppSettings.SaveConfig(false);
                chkDisable.Checked = false;
                MessageBox.Show("SteelSeries Chat & Gaming devices are visible again.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Log("Restore failed: " + ex);
                MessageBox.Show("Restore did not complete successfully. See the log file for details.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettings.SaveConfig(chkDisable.Checked);
                AppSettings.StartWithWindows = chkStartWithWindows.Checked;

                AppSettings.MarkSettingsConfirmed();

                _applySteelSeriesSetting(chkDisable.Checked);

                _ensureWatcherStarted?.Invoke();

                MessageBox.Show(
                    "Settings saved. Discord auto-mute keeps running in the background.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Log("BtnOk_Click failed: " + ex);
                MessageBox.Show("Settings did not save successfully. See the log file for details.",
                    "SteelSeries Discord Stream Fix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Hide();
        }
    }

    internal sealed class ModernButton : Button
    {
        public Color BaseColor { get; set; } = Color.FromArgb(94, 106, 246);
        public Color HoverColor { get; set; } = Color.FromArgb(112, 123, 250);
        public Color TextColor { get; set; } = Color.White;
        public Color? BorderColor { get; set; } = null;
        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            BackColor = Color.Transparent;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += (s, e) => { _pressed = true; Invalidate(); };
            MouseUp += (s, e) => { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            Color fill = _pressed ? ControlPaint.Dark(HoverColor, 0.05f) : (_hover ? HoverColor : BaseColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(10, Height / 2);

            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (BorderColor.HasValue)
                {
                    using (var pen = new Pen(BorderColor.Value, 1))
                        g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernCheckBox : CheckBox
    {
        public Color AccentColor { get; set; } = Color.FromArgb(94, 106, 246);
        public Color BorderColor { get; set; } = Color.FromArgb(58, 58, 72);
        public Color TextColor { get; set; } = Color.White;

        private const int BoxSize = 18;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Color.White);

            int boxY = (Height - BoxSize) / 2;
            var boxRect = new Rectangle(0, boxY, BoxSize, BoxSize);

            using (var path = RoundedRect(boxRect, 4))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(AccentColor))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(BorderColor, 1.5f))
                        g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(boxRect.X + 4, boxRect.Y + 9),
                        new Point(boxRect.X + 7, boxRect.Y + 13),
                        new Point(boxRect.X + 14, boxRect.Y + 5)
                    });
                }
            }

            var textRect = new Rectangle(BoxSize + 10, 0, Width - BoxSize - 10, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Invalidate();
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class SteelSeriesToggle
    {
        private const string ChatDeviceNameFragment = "SteelSeries Sonar - Chat";
        private const string GamingDeviceNameFragment = "SteelSeries Sonar - Gaming";

        public static void SetChatAndGamingVisible(bool visible)
        {
            SetEndpointVisible(ChatDeviceNameFragment, visible);
            SetEndpointVisible(GamingDeviceNameFragment, visible);
        }

        private static void SetEndpointVisible(string deviceNameFragment, bool visible)
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                bool foundAny = false;

                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
                {
                    bool isMatch;
                    string friendlyName = "<unknown>";
                    try
                    {
                        friendlyName = device.FriendlyName;
                        isMatch = friendlyName.IndexOf(deviceNameFragment, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    catch { isMatch = false; }

                    if (!isMatch)
                    {
                        device.Dispose();
                        continue;
                    }

                    foundAny = true;
                    string deviceId = device.ID;
                    device.Dispose();

                    Logger.Log($"SetEndpointVisible: attempting '{friendlyName}' (id={deviceId}) visible={visible}");

                    try
                    {
                        var policyConfig = (IPolicyConfig)new PolicyConfigClient();
                        try
                        {
                            int hr = policyConfig.SetEndpointVisibility(deviceId, visible);
                            if (hr == 0)
                            {
                                Logger.Log($"SetEndpointVisible: succeeded for '{friendlyName}' (HRESULT=0x{hr:X8})");
                            }
                            else
                            {
                                Logger.Log($"SetEndpointVisible: call returned non-zero HRESULT=0x{hr:X8} for '{friendlyName}'");
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(policyConfig);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"SetEndpointVisible failed for '{friendlyName}' ({deviceNameFragment}): " + ex);
                    }
                }

                if (!foundAny)
                {
                    Logger.Log($"SetEndpointVisible: no device matched name fragment '{deviceNameFragment}'");
                }
            }
        }
    }

    #region Undocumented Windows audio policy COM interop

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyConfigClient
    {
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat(string deviceId, bool defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string deviceId);
        [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string deviceId, bool defaultPeriod, out long defaultDevicePeriod, out long minimumDevicePeriod);
        [PreserveSig] int SetProcessingPeriod(string deviceId, ref long period);
        [PreserveSig] int GetShareMode(string deviceId, out IntPtr shareMode);
        [PreserveSig] int SetShareMode(string deviceId, IntPtr shareMode);
        [PreserveSig] int GetPropertyValue(string deviceId, ref CsPropertyKey key, out CsPropVariant value);
        [PreserveSig] int SetPropertyValue(string deviceId, ref CsPropertyKey key, ref CsPropVariant value);
        [PreserveSig] int SetDefaultEndpoint(string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility(string deviceId, bool visible);
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CsPropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CsPropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public int p2;
    }

    #endregion
}