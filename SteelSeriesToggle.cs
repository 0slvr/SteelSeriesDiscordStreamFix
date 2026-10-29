using NAudio.CoreAudioApi;
using System;
using System.Runtime.InteropServices;

namespace SteelSeriesDiscordStreamFix
{
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
                        Logger.Log($"SetEndpointVisible failed for '{friendlyName}' ({deviceNameFragment}) — " +
                                   "this can happen on Windows builds with a different IPolicyConfig vtable layout: " + ex);
                    }
                }

                if (!foundAny)
                {
                    Logger.Log($"SetEndpointVisible: no device matched name fragment '{deviceNameFragment}'");
                }
            }
        }
    }
}