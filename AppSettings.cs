using Microsoft.Win32;
using System;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
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


        private const string ValueNameWeHidDevices = "WeHidDevices";

        /// <summary>True while this app has Sonar Chat/Gaming hidden (so we only restore what we hid).</summary>
        public static bool WeHidDevices
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    {
                        if (key == null) return false;
                        object value = key.GetValue(ValueNameWeHidDevices);
                        return value != null && Convert.ToInt32(value) != 0;
                    }
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                        key?.SetValue(ValueNameWeHidDevices, value ? 1 : 0, RegistryValueKind.DWord);
                }
                catch (Exception ex) { Logger.Log("WeHidDevices write failed: " + ex.Message); }
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

        private const string ValueNameRunState = "RunState";

        /// <summary>
        /// "running" while the app is alive, "clean" after a normal exit / logoff / shutdown. If a start finds
        /// "running", the previous run ended abnormally (crash, forced kill, power loss).
        /// </summary>
        public static string RunState
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                        return key?.GetValue(ValueNameRunState) as string;
                }
                catch { return null; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                        key?.SetValue(ValueNameRunState, value ?? string.Empty, RegistryValueKind.String);
                }
                catch (Exception ex) { Logger.Log("RunState write failed: " + ex.Message); }
            }
        }

        private static int ReadInt(string name, int defaultValue, int min, int max)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    object v = key?.GetValue(name);
                    if (v == null) return defaultValue;
                    int n = Convert.ToInt32(v);
                    return Math.Max(min, Math.Min(max, n));
                }
            }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Advanced (no UI): how long after a Discord session opens an un-mute is treated as "Windows/Discord
        /// restoring the old state" and re-muted. Registry DWORD "RemuteWindowSeconds", 5-120, default 15.
        /// </summary>
        public static int RemuteWindowSeconds => ReadInt("RemuteWindowSeconds", 15, 5, 120);

        /// <summary>Advanced (no UI): max automatic re-mutes per session. Registry DWORD "MaxRemutes", 1-10, default 3.</summary>
        public static int MaxRemutes => ReadInt("MaxRemutes", 3, 1, 10);

        private const string ValueNameExtraProcessNames = "ExtraProcessNames";

        /// <summary>
        /// Advanced, optional (no UI): comma-separated extra process names (without .exe) to treat as Discord,
        /// e.g. a Discord fork. Set it in HKCU\SOFTWARE\SteelSeriesDiscordStreamFix as a string value.
        /// </summary>
        public static string ExtraProcessNames
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                        return key?.GetValue(ValueNameExtraProcessNames) as string;
                }
                catch { return null; }
            }
        }

        private const string ValueNameSkippedUpdate = "SkippedUpdateVersion";

        /// <summary>Version the user chose "Skip this version" for. No notifications for it; a newer one notifies again.</summary>
        public static string SkippedUpdateVersion
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                        return key?.GetValue(ValueNameSkippedUpdate) as string;
                }
                catch { return null; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                    {
                        if (string.IsNullOrEmpty(value)) key?.DeleteValue(ValueNameSkippedUpdate, false);
                        else key?.SetValue(ValueNameSkippedUpdate, value, RegistryValueKind.String);
                    }
                }
                catch (Exception ex) { Logger.Log("SkippedUpdateVersion write failed: " + ex.Message); }
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
}