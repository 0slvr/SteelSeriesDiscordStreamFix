using NAudio.CoreAudioApi;
using System;
using System.Runtime.InteropServices;

namespace SteelSeriesDiscordStreamFix
{
    internal static class SteelSeriesToggle
    {
        private const string ChatDeviceNameFragment = "SteelSeries Sonar - Chat";
        private const string GamingDeviceNameFragment = "SteelSeries Sonar - Gaming";

        /// <summary>Returns false if any matching device could not be changed.</summary>
        public static bool SetChatAndGamingVisible(bool visible)
        {
            bool a = SetEndpointVisible(ChatDeviceNameFragment, visible);
            bool b = SetEndpointVisible(GamingDeviceNameFragment, visible);
            return a && b;
        }

        /// <summary>
        /// True if at least one Sonar Chat/Gaming device is not in the wanted state
        /// (visible = Active, hidden = anything else). Lets callers skip no-op applies,
        /// which is what breaks the hide -> notification -> hide loop.
        /// </summary>
        public static bool NeedsChange(bool visible)
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
                    {
                        try
                        {
                            string name = device.FriendlyName;
                            bool match = name.IndexOf(ChatDeviceNameFragment, StringComparison.OrdinalIgnoreCase) >= 0
                                      || name.IndexOf(GamingDeviceNameFragment, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match) continue;
                            DeviceState state = device.State;
                            // A device that is not present at all cannot be shown or hidden: ignore it, otherwise
                            // it would look "wrong" forever and trigger pointless re-applies.
                            if (state == DeviceState.NotPresent) continue;
                            bool isActive = state == DeviceState.Active;
                            if (isActive != visible) return true;
                        }
                        catch { }
                        finally { device.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("NeedsChange failed: " + ex.Message);
                return true;
            }
            return false;
        }

        private static bool SetEndpointVisible(string deviceNameFragment, bool visible)
        {
            bool allOk = true;
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
                                allOk = false;
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
                        allOk = false;
                        Logger.Log($"SetEndpointVisible failed for '{friendlyName}' ({deviceNameFragment}) — " +
                                   "this can happen on Windows builds with a different IPolicyConfig vtable layout: " + ex);
                    }
                }

                if (!foundAny)
                {
                    Logger.Log($"SetEndpointVisible: no device matched name fragment '{deviceNameFragment}'");
                }
            }
            return allOk;
        }
    }
}