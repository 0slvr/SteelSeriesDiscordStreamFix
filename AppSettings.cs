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