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

        public event Action DeviceTopologyChanged;

        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly object _lock = new object();
        private readonly object _topologyLock = new object();
        private readonly Dictionary<int, AudioSessionControl> _trackedSessions = new Dictionary<int, AudioSessionControl>();
        private readonly Dictionary<int, DiscordSessionEventsHandler> _sessionEventHandlers = new Dictionary<int, DiscordSessionEventsHandler>();

        private MMDevice _targetDevice;
        private AudioSessionManager _sessionManager;
        private System.Windows.Forms.Timer _safetyNetTimer;
        private System.Threading.Timer _topologyDebounceTimer;
        private volatile bool _disposed;

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
            if (_disposed) return;

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
            if (_disposed) return;

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
                if (_disposed) return;
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

                try { kv.Value.Dispose(); } catch { }
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
                if (_disposed) return;

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

        public void OnDeviceAdded(string deviceId)
        {
            ScheduleTopologyRecheck();
            ScheduleTopologyChangedNotification();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            ScheduleTopologyRecheck();
            ScheduleTopologyChangedNotification();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            ScheduleTopologyRecheck();
            ScheduleTopologyChangedNotification();
        }

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
                        if (_disposed) return;
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

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
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
                if (_owner._disposed) return;
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
                try { _session.Dispose(); } catch { }
                Logger.Log($"Discord session (pid={_pid}) disconnected: {disconnectReason}");
            }

            public void OnDisplayNameChanged(string displayName) { }
            public void OnIconPathChanged(string iconPath) { }
            public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
            public void OnGroupingParamChanged(ref Guid groupingId) { }
            public void OnStateChanged(AudioSessionState state) { }
        }
    }
}