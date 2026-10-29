using System;
using System.Runtime.InteropServices;

namespace SteelSeriesDiscordStreamFix
{
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
}