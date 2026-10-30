using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SteelSeriesDiscordStreamFix
{
    internal sealed class MuteWatcherService : IMMNotificationClient, IDisposable
    {
        public const string MicEndpointNameFragment = "SteelSeries Sonar - Microphone";

        // Tried in order. The second, shorter fragment still matches if SteelSeries
        // renames the product prefix (e.g. "SteelSeries GG Sonar - Microphone").
        private static readonly string[] EndpointNameFragments =
        {
            MicEndpointNameFragment,
            "Sonar - Microphone"
        };

        // Exact process names (without ".exe"): stable, PTB, Canary, dev builds and the Vesktop client.
        private static readonly HashSet<string> DiscordProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment", "Vesktop",
              "Equibop", "Legcord", "ArmCord", "WebCord", "Vencord", "Dorion" };

        // Explicit list only: a process that merely starts with "Discord" is NOT muted. A new Discord channel
        // or fork can be added without a new release through the "ExtraProcessNames" registry value.
        private static bool IsDiscordName(string name, HashSet<string> extra)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return DiscordProcessNames.Contains(name) || extra.Contains(name);
        }

        // After a Discord session is first muted we defend that mute (Windows/Discord may restore the old
        // volume state right after the session opens). Once this window has passed, or after this many
        // re-mutes, an un-mute is treated as a deliberate user action and is left alone. Without this limit
        // the app fights the user forever and Discord can never be un-muted (the mute state is shared with
        // Discord's real audio on many systems, so "re-mute instantly" silences ALL of Discord).
        // Both values can be tuned in the registry (RemuteWindowSeconds / MaxRemutes, see AppSettings) and are
        // re-read every time the device is (re)bound.
        private static volatile int _remuteWindowSeconds = 15;
        private static volatile int _maxRemutes = 3;
        private static TimeSpan RemuteWindow => TimeSpan.FromSeconds(_remuteWindowSeconds);

        private const int TickIntervalMs = 30 * 1000;
        private static readonly TimeSpan BoundCheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NotFoundNotifyDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MuteProblemThrottle = TimeSpan.FromMinutes(10);

        public event Action DeviceTopologyChanged;

        /// <summary>Raised (on a thread-pool thread) when <see cref="StatusText"/> changes.</summary>
        public event Action StatusChanged;

        /// <summary>
        /// Raised (on a thread-pool thread) when something the user should know about goes wrong,
        /// e.g. the Sonar microphone device can't be found or Discord can't be muted.
        /// </summary>
        public event Action<string> ProblemDetected;

        private volatile string _statusText = "Not started yet (press Save to begin).";
        public string StatusText => _statusText;

        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly object _lock = new object();
        private readonly object _topologyLock = new object();
        // Guards ONLY the two session dictionaries. Never held while calling into COM, and it is the only
        // lock the WASAPI callback handlers take, so a callback can never deadlock against Bind/Dispose.
        private readonly object _sessionsLock = new object();
        private volatile string _targetDeviceId;
        private int _safetyNetRunning;
        // Keyed by the session key (see GetSessionKey), NOT by PID: one Discord process can own several sessions
        // on the same device, and a second session of an already-tracked PID must still be muted.
        private readonly Dictionary<string, AudioSessionControl> _trackedSessions = new Dictionary<string, AudioSessionControl>();
        private readonly Dictionary<string, DiscordSessionEventsHandler> _sessionEventHandlers = new Dictionary<string, DiscordSessionEventsHandler>();
        // Extra process names from the registry (value "ExtraProcessNames", comma separated) for Discord forks.
        private volatile HashSet<string> _extraProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Keys (see GetSessionKey) of sessions whose mute WE applied. On exit we only undo these, so a session
        // the user muted themselves is never un-muted behind their back. Guarded by _sessionsLock.
        private readonly HashSet<string> _mutedByUsKeys = new HashSet<string>();
        // Keys of sessions the user deliberately un-muted. They survive a rebind (Sonar restart, device event)
        // so we do not re-mute a session the user already took control of. Guarded by _sessionsLock.
        private readonly HashSet<string> _userUnmutedKeys = new HashSet<string>();
        private const int MaxRememberedKeys = 200;

        private MMDevice _targetDevice;
        private AudioSessionManager _sessionManager;
        private System.Windows.Forms.Timer _safetyNetTimer;
        private System.Threading.Timer _topologyDebounceTimer;
        // Coalesces the burst of device events (Sonar restart sends dozens) into ONE re-check.
        private System.Threading.Timer _recheckTimer;
        private bool _recheckOurs;
        private const int RecheckDebounceMs = 1000;
        private volatile bool _disposed;

        private DateTime _lastDeviceCheckUtc = DateTime.MinValue;
        private DateTime? _notFoundSinceUtc;
        private bool _notFoundNotified;
        private bool _notFoundLogged;
        private DateTime _lastMuteProblemUtc = DateTime.MinValue;

        public void Start()
        {
            _enumerator.RegisterEndpointNotificationCallback(this);

            SetStatus("Starting...");

            // Enumerating audio endpoints can be slow (audio service still starting after login, a stuck driver),
            // so the first bind runs on a worker thread instead of freezing the window that pressed Save.
            Task.Run(() =>
            {
                try
                {
                    BindToTargetDevice();
                }
                catch (Exception ex)
                {
                    Logger.Log("Start: initial BindToTargetDevice failed, will retry via safety net/device events: " + ex);
                }
            });

            // Ticks every 30 s. While the device is missing, every tick retries the bind;
            // once bound, the real device check only runs every 5 minutes.
            _safetyNetTimer = new System.Windows.Forms.Timer { Interval = TickIntervalMs };
            _safetyNetTimer.Tick += (s, e) =>
            {
                // Run the COM work off the UI thread so a slow audio driver can't freeze the window.
                if (System.Threading.Interlocked.Exchange(ref _safetyNetRunning, 1) == 1) return;
                Task.Run(() =>
                {
                    try { SafetyNetCheck(); }
                    catch (Exception ex) { Logger.Log("SafetyNetCheck failed: " + ex); }
                    finally { System.Threading.Interlocked.Exchange(ref _safetyNetRunning, 0); }
                });
            };
            _safetyNetTimer.Start();

            Logger.Log("MuteWatcherService started (event-driven; retries every 30 s while the device is missing, 5-minute safety net once bound).");
        }

        private void BindToTargetDevice()
        {
            lock (_lock)
            {
                if (_disposed) return;

                UnbindCurrentDevice();
                _extraProcessNames = LoadExtraProcessNames();
                LoadRemuteSettings();

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
                    if (!_notFoundSinceUtc.HasValue) _notFoundSinceUtc = DateTime.UtcNow;

                    if (!_notFoundLogged)
                    {
                        _notFoundLogged = true;
                        Logger.Log("MuteWatcherService: target microphone endpoint not found (will retry on every device-change event and every 30 s).");
                    }

                    SetStatus("Sonar microphone device not found. Is SteelSeries GG / Sonar running? Retrying every 30 seconds.");
                    return;
                }

                _targetDevice = found;
                _targetDeviceId = found.ID;
                _notFoundSinceUtc = null;
                _notFoundNotified = false;
                _notFoundLogged = false;
                _lastDeviceCheckUtc = DateTime.UtcNow;
                Logger.Log($"MuteWatcherService: bound to '{_targetDevice.FriendlyName}' (id={_targetDevice.ID})");
                SetStatus($"Connected to \"{_targetDevice.FriendlyName}\" - Discord auto-mute is active.");

                try
                {
                    _sessionManager = _targetDevice.AudioSessionManager;
                    _sessionManager.OnSessionCreated += OnSessionCreated;

                    SessionCollection sessions = _sessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        // One broken session must not stop the others from being tracked.
                        AudioSessionControl s = null;
                        try
                        {
                            s = sessions[i];
                            if (!TrackIfDiscord(s)) { try { s.Dispose(); } catch { } }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("BindToTargetDevice: session #" + i + " skipped: " + ex.Message);
                            try { s?.Dispose(); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("BindToTargetDevice: session manager setup failed: " + ex);
                    SetStatus("Connected, but could not watch audio sessions - see the log file.");
                    RaiseProblem("Could not watch audio sessions on the Sonar microphone device. Discord may not be muted. See the log file for details.");
                }
            }
        }

        private MMDevice FindTargetDevice()
        {
            var devices = new List<MMDevice>();
            foreach (MMDevice device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                devices.Add(device);

            MMDevice match = null;
            foreach (string fragment in EndpointNameFragments)
            {
                foreach (MMDevice device in devices)
                {
                    string name;
                    try { name = device.FriendlyName; }
                    catch { continue; }

                    if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = device;
                        break;
                    }
                }
                if (match != null) break;
            }

            foreach (MMDevice device in devices)
            {
                if (!ReferenceEquals(device, match))
                    device.Dispose();
            }
            return match;
        }

        private void OnSessionCreated(object sender, IAudioSessionControl newSession)
        {
            if (_disposed) return;

            // This is a WASAPI notification callback: do not call back into the audio engine from inside it
            // (reading the PID, muting, registering handlers). Hand the work to the thread pool, like the other
            // callbacks do.
            Task.Run(() =>
            {
                try
                {
                    if (_disposed) return;
                    var session = new AudioSessionControl(newSession);
                    if (!TrackIfDiscord(session)) { try { session.Dispose(); } catch { } }
                }
                catch (Exception ex)
                {
                    Logger.Log("OnSessionCreated failed: " + ex.Message);
                }
            });
        }

        private bool TrackIfDiscord(AudioSessionControl session)
        {
            if (_disposed) return false;

            int pid;
            try { pid = (int)session.GetProcessID; }
            catch { return false; }

            if (pid == 0) return false;

            string processName;
            try
            {
                using (Process proc = Process.GetProcessById(pid))
                    processName = proc.ProcessName;
            }
            catch
            {
                return false;
            }

            if (!IsDiscordName(processName, _extraProcessNames)) return false;

            string key = GetSessionKey(session, pid);

            // The user already un-muted this very session on purpose (we are just re-binding after a device
            // event): leave it alone instead of starting a new protection window.
            bool userUnmuted;
            lock (_sessionsLock) { userUnmuted = _userUnmutedKeys.Contains(key); }
            if (userUnmuted)
            {
                Logger.Log($"MuteWatcherService: Discord session (pid={pid}) was un-muted by the user earlier — not re-muting it.");
                return false;
            }

            var handler = new DiscordSessionEventsHandler(pid, key, this, session);
            lock (_sessionsLock)
            {
                if (_disposed) return false;
                if (_trackedSessions.ContainsKey(key)) return false;
                _trackedSessions[key] = session;
                _sessionEventHandlers[key] = handler;
            }

            try
            {
                MuteSession(session, key, $"new Discord session detected (pid={pid})");
                session.RegisterEventClient(handler);
                return true;
            }
            catch (Exception ex)
            {
                // Do not leave a half-registered session behind (tracked, but no events would ever arrive).
                Logger.Log($"TrackIfDiscord: could not register session (pid={pid}): " + ex.Message);
                lock (_sessionsLock)
                {
                    _trackedSessions.Remove(key);
                    _sessionEventHandlers.Remove(key);
                }
                return false;
            }
        }

        private static void LoadRemuteSettings()
        {
            try
            {
                _remuteWindowSeconds = AppSettings.RemuteWindowSeconds;
                _maxRemutes = AppSettings.MaxRemutes;
                if (_remuteWindowSeconds != 15 || _maxRemutes != 3)
                    Logger.Log($"MuteWatcherService: custom re-mute settings: window={_remuteWindowSeconds}s, max re-mutes={_maxRemutes}.");
            }
            catch (Exception ex) { Logger.Log("LoadRemuteSettings failed: " + ex.Message); }
        }

        private static HashSet<string> LoadExtraProcessNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string raw = AppSettings.ExtraProcessNames;
                if (string.IsNullOrWhiteSpace(raw)) return set;
                foreach (string part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = part.Trim();
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    if (name.Length > 0) set.Add(name);
                }
                if (set.Count > 0) Logger.Log("MuteWatcherService: extra process names from settings: " + string.Join(", ", set));
            }
            catch (Exception ex) { Logger.Log("LoadExtraProcessNames failed: " + ex.Message); }
            return set;
        }

        /// <summary>
        /// Stable identity of a session across re-binds: the WASAPI session instance id, or the PID if
        /// Windows does not give us one.
        /// </summary>
        private static string GetSessionKey(AudioSessionControl session, int pid)
        {
            try
            {
                string id = session.GetSessionInstanceIdentifier;
                if (!string.IsNullOrEmpty(id)) return id;
            }
            catch { }
            return "pid:" + pid;
        }

        private void RememberUserUnmute(string key)
        {
            lock (_sessionsLock)
            {
                _mutedByUsKeys.Remove(key);
                if (_userUnmutedKeys.Count >= MaxRememberedKeys)
                {
                    // Drop only a few old entries instead of forgetting every choice the user made.
                    var old = new List<string>();
                    foreach (string k in _userUnmutedKeys) { old.Add(k); if (old.Count >= 20) break; }
                    foreach (string k in old) _userUnmutedKeys.Remove(k);
                }
                _userUnmutedKeys.Add(key);
            }
        }

        private void MuteSession(AudioSessionControl session, string key, string reason)
        {
            try
            {
                if (!session.SimpleAudioVolume.Mute)
                {
                    session.SimpleAudioVolume.Mute = true;
                    lock (_sessionsLock) { _mutedByUsKeys.Add(key); }
                    Logger.Log("MuteWatcherService: muted Discord — " + reason);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("MuteSession failed: " + ex.Message);

                // The session may simply have closed (Discord call ended / app restarted):
                // that is not a real failure, so do not bother the user with a balloon.
                bool stillTracked;
                lock (_sessionsLock) { stillTracked = _trackedSessions.ContainsValue(session); }
                if (!stillTracked || _disposed) return;

                if (DateTime.UtcNow - _lastMuteProblemUtc >= MuteProblemThrottle)
                {
                    _lastMuteProblemUtc = DateTime.UtcNow;
                    RaiseProblem("Could not mute Discord on the Sonar microphone device: " + ex.Message);
                }
            }
        }

        private void UnbindCurrentDevice()
        {
            if (_sessionManager != null)
            {
                try { _sessionManager.OnSessionCreated -= OnSessionCreated; } catch { }
                _sessionManager = null;
            }

            KeyValuePair<string, AudioSessionControl>[] tracked;
            Dictionary<string, DiscordSessionEventsHandler> handlers;
            lock (_sessionsLock)
            {
                tracked = new List<KeyValuePair<string, AudioSessionControl>>(_trackedSessions).ToArray();
                handlers = new Dictionary<string, DiscordSessionEventsHandler>(_sessionEventHandlers);
                _sessionEventHandlers.Clear();
                _trackedSessions.Clear();
            }

            foreach (var kv in tracked)
            {
                if (handlers.TryGetValue(kv.Key, out var h))
                {
                    try { kv.Value.UnRegisterEventClient(h); } catch { }
                }

                try { kv.Value.Dispose(); } catch { }
            }
            _targetDevice?.Dispose();
            _targetDevice = null;
            _targetDeviceId = null;
        }

        private void SafetyNetCheck()
        {
            string problem = null;

            lock (_lock)
            {
                if (_disposed) return;

                bool bound = _targetDevice != null;

                // Once bound, only do the (cheap) device check every 5 minutes.
                if (bound && DateTime.UtcNow - _lastDeviceCheckUtc < BoundCheckInterval) return;
                _lastDeviceCheckUtc = DateTime.UtcNow;

                bool stillValid = bound && DeviceStillExists(_targetDevice.ID);
                if (!stillValid)
                {
                    if (bound)
                        Logger.Log("SafetyNetCheck: target device missing or changed - rebinding.");

                    try
                    {
                        BindToTargetDevice();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("SafetyNetCheck: rebind failed, will retry next tick: " + ex);
                    }
                }

                if (_targetDevice == null
                    && _notFoundSinceUtc.HasValue
                    && !_notFoundNotified
                    && DateTime.UtcNow - _notFoundSinceUtc.Value >= NotFoundNotifyDelay)
                {
                    _notFoundNotified = true;
                    problem = "The \"SteelSeries Sonar - Microphone\" device was not found, so Discord is NOT being auto-muted. " +
                              "Make sure SteelSeries GG / Sonar is running.";
                }
            }

            if (problem != null) RaiseProblem(problem);
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

        public void OnDeviceAdded(string deviceId)
        {
            ScheduleTopologyRecheck(deviceId);
            ScheduleTopologyChangedNotification();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            ScheduleTopologyRecheck(deviceId);
            ScheduleTopologyChangedNotification();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            ScheduleTopologyRecheck(deviceId);
            ScheduleTopologyChangedNotification();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void ScheduleTopologyRecheck(string changedDeviceId)
        {
            if (_disposed) return;

            // Windows sends a burst of events when Sonar restarts. Instead of one rebind per event, wait until
            // the burst is over and do a single re-check.
            lock (_topologyLock)
            {
                if (_disposed) return;

                // If the event concerns our own endpoint (Sonar restart, audio service restart, resume from
                // sleep) the device ID is unchanged but the old session manager is dead, so we must rebind
                // even though the device looks "Active" again.
                if (changedDeviceId != null && changedDeviceId == _targetDeviceId)
                    _recheckOurs = true;

                if (_recheckTimer == null)
                    _recheckTimer = new System.Threading.Timer(_ => RunTopologyRecheck(), null, RecheckDebounceMs, System.Threading.Timeout.Infinite);
                else
                    _recheckTimer.Change(RecheckDebounceMs, System.Threading.Timeout.Infinite);
            }
        }

        private void RunTopologyRecheck()
        {
            bool isOurs;
            lock (_topologyLock)
            {
                isOurs = _recheckOurs;
                _recheckOurs = false;
            }

            if (_disposed) return;

            try
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    bool currentStillValid = _targetDevice != null && DeviceStillExists(_targetDevice.ID);
                    if (!currentStillValid || isOurs)
                        BindToTargetDevice();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("RunTopologyRecheck failed: " + ex);
            }
        }

        private void ScheduleTopologyChangedNotification()
        {
            lock (_topologyLock)
            {
                if (_disposed) return;

                _topologyDebounceTimer?.Dispose();
                _topologyDebounceTimer = new System.Threading.Timer(_ =>
                {
                    if (_disposed) return;
                    try
                    {
                        Logger.Log("MuteWatcherService: audio device topology changed — notifying listeners.");
                        DeviceTopologyChanged?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("DeviceTopologyChanged handler failed: " + ex);
                    }
                }, null, 1500, System.Threading.Timeout.Infinite);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusText == text) return;
            _statusText = text;
            Raise(StatusChanged);
        }

        private void RaiseProblem(string message)
        {
            Logger.Log("PROBLEM: " + message);
            var handler = ProblemDetected;
            if (handler == null || _disposed) return;
            Task.Run(() =>
            {
                try { handler(message); }
                catch (Exception ex) { Logger.Log("ProblemDetected handler failed: " + ex.Message); }
            });
        }

        private void Raise(Action handler)
        {
            if (handler == null || _disposed) return;
            Task.Run(() =>
            {
                try { handler(); }
                catch (Exception ex) { Logger.Log("StatusChanged handler failed: " + ex.Message); }
            });
        }

        private int _disposeStarted;

        public void Dispose() { Shutdown(3000); }

        /// <summary>
        /// Stops the watcher and un-mutes what WE muted. <paramref name="lockWaitMs"/> bounds the wait for a COM
        /// call that may be stuck (use a short value while Windows is shutting down).
        /// </summary>
        public void Shutdown(int lockWaitMs)
        {
            if (System.Threading.Interlocked.Exchange(ref _disposeStarted, 1) == 1) return;
            _disposed = true;
            // A stuck Sonar/audio driver can leave a COM call running while it holds _lock.
            // Wait a short while, then carry on so closing the app can never freeze the UI.
            bool gotLock = System.Threading.Monitor.TryEnter(_lock, lockWaitMs);
            if (!gotLock) Logger.Log("Dispose: could not take the lock within " + lockWaitMs + " ms (COM call stuck?) — continuing anyway.");
            try
            {
                // We are going away, so stop forcing Discord muted: undo OUR mutes on the way out.
                // A session the user muted themselves (or un-muted on purpose) is left exactly as it is.
                try
                {
                    var toUnmute = new List<AudioSessionControl>();
                    lock (_sessionsLock)
                    {
                        foreach (var kv in _trackedSessions)
                        {
                            DiscordSessionEventsHandler h;
                            if (_sessionEventHandlers.TryGetValue(kv.Key, out h) && _mutedByUsKeys.Contains(h.Key))
                                toUnmute.Add(kv.Value);
                        }
                    }
                    foreach (var s in toUnmute)
                    {
                        try { s.SimpleAudioVolume.Mute = false; } catch { }
                    }
                }
                catch { }
                _safetyNetTimer?.Stop();
                _safetyNetTimer?.Dispose();
                lock (_topologyLock)
                {
                    _topologyDebounceTimer?.Dispose();
                    _recheckTimer?.Dispose();
                }
                try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
                if (gotLock)
                {
                    UnbindCurrentDevice();
                    _enumerator.Dispose();
                }
            }
            finally
            {
                if (gotLock) System.Threading.Monitor.Exit(_lock);
            }
        }

        private sealed class DiscordSessionEventsHandler : IAudioSessionEventsHandler
        {
            private readonly int _pid;
            public string Key { get; }
            private readonly MuteWatcherService _owner;
            private readonly AudioSessionControl _session;
            private readonly DateTime _trackedAtUtc = DateTime.UtcNow;
            private int _remuteCount;
            private int _respectLogged;
            private volatile bool _closed;

            public DiscordSessionEventsHandler(int pid, string key, MuteWatcherService owner, AudioSessionControl session)
            {
                _pid = pid;
                Key = key;
                _owner = owner;
                _session = session;
            }

            public void OnVolumeChanged(float volume, bool isMuted)
            {
                if (_owner._disposed || isMuted) return;

                bool inWindow = DateTime.UtcNow - _trackedAtUtc <= RemuteWindow;
                if (!inWindow)
                {
                    // Deliberate un-mute (e.g. the user un-muting Discord in the Windows mixer): respect it,
                    // and remember it so a later rebind does not re-mute this session.
                    _owner.RememberUserUnmute(Key);
                    if (System.Threading.Interlocked.Exchange(ref _respectLogged, 1) == 0)
                        Logger.Log($"MuteWatcherService: Discord (pid={_pid}) was un-muted {(DateTime.UtcNow - _trackedAtUtc).TotalSeconds:0}s after its session opened " +
                                   $"(protection window = {_remuteWindowSeconds}s) — treating it as a deliberate un-mute and leaving it un-muted. " +
                                   "If Discord restores its state later than this on your PC, raise the registry DWORD RemuteWindowSeconds.");
                    return;
                }

                if (_remuteCount >= _maxRemutes)
                {
                    // We stop fighting, but this is NOT proof that the user did it: Windows/Discord may be
                    // restoring the old state. So it is deliberately not remembered as a user choice, and a
                    // later rebind will try to mute this session again.
                    if (System.Threading.Interlocked.Exchange(ref _respectLogged, 1) == 0)
                        Logger.Log($"WARNING: Discord (pid={_pid}) was un-muted again after {_maxRemutes} automatic re-mutes — giving up on this session for now " +
                                   "(Discord is NOT muted on the Sonar microphone device right now). A rebind (device change / Sonar restart) will try again. " +
                                   "To allow more attempts, raise the registry DWORD MaxRemutes.");
                    return;
                }

                _remuteCount++;
                // WASAPI forbids calling back into the audio engine from inside a notification
                // callback (can stall/deadlock the audio engine and freeze Windows volume UI),
                // so hand the re-mute to the thread pool.
                Task.Run(() =>
                {
                    if (!_owner._disposed && !_closed)
                        _owner.MuteSession(_session, Key, "session was un-muted right after it opened — re-muting");
                });
            }

            public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
            {
                _closed = true;
                lock (_owner._sessionsLock)
                {
                    _owner._trackedSessions.Remove(Key);
                    _owner._sessionEventHandlers.Remove(Key);
                    // The session is gone for good: forget it so these sets cannot grow forever.
                    _owner._mutedByUsKeys.Remove(Key);
                    _owner._userUnmutedKeys.Remove(Key);
                }
                // Do not release the COM session from inside its own callback.
                Task.Run(() =>
                {
                    try { _session.UnRegisterEventClient(this); } catch { }
                    try { _session.Dispose(); } catch { }
                    Logger.Log($"Discord session (pid={_pid}) disconnected: {disconnectReason}");
                });
            }

            public void OnDisplayNameChanged(string displayName) { }
            public void OnIconPathChanged(string iconPath) { }
            public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
            public void OnGroupingParamChanged(ref Guid groupingId) { }
            public void OnStateChanged(AudioSessionState state) { }
        }
    }
}