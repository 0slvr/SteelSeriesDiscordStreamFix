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

        // Exact process names (without ".exe"). Only the stable Discord client is muted.
        // To also mute other builds, add "DiscordPTB" and/or "DiscordCanary" here.
        private static readonly HashSet<string> DiscordProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Discord" };

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
        private readonly Dictionary<int, AudioSessionControl> _trackedSessions = new Dictionary<int, AudioSessionControl>();
        private readonly Dictionary<int, DiscordSessionEventsHandler> _sessionEventHandlers = new Dictionary<int, DiscordSessionEventsHandler>();

        private MMDevice _targetDevice;
        private AudioSessionManager _sessionManager;
        private System.Windows.Forms.Timer _safetyNetTimer;
        private System.Threading.Timer _topologyDebounceTimer;
        private volatile bool _disposed;

        private DateTime _lastDeviceCheckUtc = DateTime.MinValue;
        private DateTime? _notFoundSinceUtc;
        private bool _notFoundNotified;
        private bool _notFoundLogged;
        private DateTime _lastMuteProblemUtc = DateTime.MinValue;

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
                        var s = sessions[i];
                        if (!TrackIfDiscord(s)) { try { s.Dispose(); } catch { } }
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

            try
            {
                var session = new AudioSessionControl(newSession);
                if (!TrackIfDiscord(session)) { try { session.Dispose(); } catch { } }
            }
            catch (Exception ex)
            {
                Logger.Log("OnSessionCreated failed: " + ex.Message);
            }
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

            if (!DiscordProcessNames.Contains(processName)) return false;

            var handler = new DiscordSessionEventsHandler(pid, this, session);
            lock (_sessionsLock)
            {
                if (_disposed) return false;
                if (_trackedSessions.ContainsKey(pid)) return false;
                _trackedSessions[pid] = session;
                _sessionEventHandlers[pid] = handler;
            }

            MuteSession(session, $"new Discord session detected (pid={pid})");
            session.RegisterEventClient(handler);
            return true;
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

            KeyValuePair<int, AudioSessionControl>[] tracked;
            Dictionary<int, DiscordSessionEventsHandler> handlers;
            lock (_sessionsLock)
            {
                tracked = new List<KeyValuePair<int, AudioSessionControl>>(_trackedSessions).ToArray();
                handlers = new Dictionary<int, DiscordSessionEventsHandler>(_sessionEventHandlers);
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
            Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        if (_disposed) return;
                        // If the event concerns our own endpoint (Sonar restart, audio service restart, resume
                        // from sleep) the device ID is unchanged but the old session manager is dead, so rebind
                        // even though the device looks "Active" again.
                        bool isOurs = changedDeviceId != null && changedDeviceId == _targetDeviceId;
                        bool currentStillValid = _targetDevice != null && DeviceStillExists(_targetDevice.ID);
                        if (!currentStillValid || isOurs)
                            BindToTargetDevice();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("ScheduleTopologyRecheck failed: " + ex);
                }
            });
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

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                // We are going away, so stop forcing Discord muted: undo our mute on the way out.
                try
                {
                    AudioSessionControl[] sessions;
                    lock (_sessionsLock) { sessions = new List<AudioSessionControl>(_trackedSessions.Values).ToArray(); }
                    foreach (var s in sessions)
                    {
                        try { s.SimpleAudioVolume.Mute = false; } catch { }
                    }
                }
                catch { }
                _safetyNetTimer?.Stop();
                _safetyNetTimer?.Dispose();
                lock (_topologyLock) { _topologyDebounceTimer?.Dispose(); }
                try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
                UnbindCurrentDevice();
                _enumerator.Dispose();
            }
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
                if (_owner._disposed || isMuted) return;
                // WASAPI forbids calling back into the audio engine from inside a notification
                // callback (can stall/deadlock the audio engine and freeze Windows volume UI),
                // so hand the re-mute to the thread pool.
                Task.Run(() =>
                {
                    if (!_owner._disposed)
                        _owner.MuteSession(_session, "session was un-muted externally — re-muting");
                });
            }

            public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
            {
                lock (_owner._sessionsLock)
                {
                    _owner._trackedSessions.Remove(_pid);
                    _owner._sessionEventHandlers.Remove(_pid);
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