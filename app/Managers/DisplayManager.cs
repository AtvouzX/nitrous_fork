using System;
using System.Runtime.InteropServices;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static class DisplayManager
{
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_DISPLAYFREQUENCY = 0x00400000;
    public const int CDS_UPDATEREGISTRY = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODEW lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string? lpszDeviceName, ref DEVMODEW lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

    private static int _cachedMaxRefreshRate = 0;

    public static int GetPrimaryMaxRefreshRate()
    {
        if (_cachedMaxRefreshRate > 0) return _cachedMaxRefreshRate;

        int maxHz = 60;
        try
        {
            var mode = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
            if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode))
            {
                int currentWidth = mode.dmPelsWidth;
                int currentHeight = mode.dmPelsHeight;
                int i = 0;

                var testMode = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
                while (EnumDisplaySettingsW(null, i++, ref testMode))
                {
                    if (testMode.dmPelsWidth == currentWidth && testMode.dmPelsHeight == currentHeight)
                    {
                        if (testMode.dmDisplayFrequency > maxHz)
                            maxHz = testMode.dmDisplayFrequency;
                    }
                }
            }
        }
        catch { }

        _cachedMaxRefreshRate = maxHz;
        return maxHz;
    }

    public static void ApplyRefreshProfile(RefreshProfile profile, bool isAcPower)
    {
        try
        {
            int maxHz = GetPrimaryMaxRefreshRate();
            int targetHz = profile switch
            {
                RefreshProfile.Hz60 => 60,
                RefreshProfile.MaxHz => maxHz,
                RefreshProfile.Auto => isAcPower ? maxHz : 60,
                _ => 60
            };

            var mode = new DEVMODEW { dmSize = (short)Marshal.SizeOf<DEVMODEW>() };
            if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency != targetHz)
            {
                mode.dmFields = DM_DISPLAYFREQUENCY;
                mode.dmDisplayFrequency = targetHz;
                ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            }
        }
        catch { }
    }
}
