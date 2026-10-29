using System;
using System.Runtime.InteropServices;
using Nitrous.Enums;
using Nitrous.Interop;

namespace Nitrous.Managers;

public static class DisplayManager
{
    private static int _cachedMaxRefreshRate = 0;

    public static int GetPrimaryMaxRefreshRate()
    {
        if (_cachedMaxRefreshRate > 0) return _cachedMaxRefreshRate;

        int maxHz = 60;
        try
        {
            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            if (User32.EnumDisplaySettingsW(null, User32.ENUM_CURRENT_SETTINGS, ref mode))
            {
                int currentWidth = mode.dmPelsWidth;
                int currentHeight = mode.dmPelsHeight;
                int i = 0;

                var testMode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
                while (User32.EnumDisplaySettingsW(null, i++, ref testMode))
                {
                    if (testMode.dmPelsWidth == currentWidth && testMode.dmPelsHeight == currentHeight)
                    {
                        if (testMode.dmDisplayFrequency > maxHz)
                            maxHz = testMode.dmDisplayFrequency;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"EnumDisplaySettings failed to query refresh rate: {ex.Message}", "Display");
        }

        _cachedMaxRefreshRate = maxHz;
        return maxHz;
    }

    public static bool IsRefreshSupported(int width, int height, int targetHz)
    {
        try
        {
            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            for (int i = 0; User32.EnumDisplaySettingsW(null, i, ref mode); i++)
            {
                if (mode.dmPelsWidth == width &&
                    mode.dmPelsHeight == height &&
                    mode.dmDisplayFrequency == targetHz)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"IsRefreshSupported check failed: {ex.Message}", "Display");
        }

        return false;
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

            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            if (!User32.EnumDisplaySettingsW(null, User32.ENUM_CURRENT_SETTINGS, ref mode))
            {
                LogManager.Warn("Display: could not read current display mode.", "Display");
                return;
            }

            if (mode.dmDisplayFrequency == targetHz) return;

            // Do not ask the driver for a mode it did not advertise.
            if (!IsRefreshSupported(mode.dmPelsWidth, mode.dmPelsHeight, targetHz))
            {
                LogManager.Warn($"Display: {targetHz}Hz is not supported at {mode.dmPelsWidth}x{mode.dmPelsHeight}; leaving current mode unchanged.", "Display");
                return;
            }

            LogManager.Info($"Switching display refresh rate from {mode.dmDisplayFrequency}Hz to {targetHz}Hz (Profile: {profile}, AC: {isAcPower})", "Display");

            // 1. Try Windows CCD API (Connecting and Configuring Displays) - the modern standard for Windows 10/11 internal laptop panels
            if (TryApplyRefreshViaCcd(targetHz))
            {
                return;
            }

            // 2. Fall back to ChangeDisplaySettingsExW with preserved resolution and color depth flags
            mode.dmFields |= User32.DM_DISPLAYFREQUENCY | User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT | User32.DM_BITSPERPEL;
            mode.dmDisplayFrequency = targetHz;

            int ret = User32.ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (ret != 0 && !string.IsNullOrEmpty(mode.dmDeviceName))
            {
                // Retry with explicit device name if null returned an error
                ret = User32.ChangeDisplaySettingsExW(mode.dmDeviceName, ref mode, IntPtr.Zero, User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
            }

            if (ret != 0)
            {
                LogManager.Warn($"ChangeDisplaySettingsExW returned error code {ret}", "Display");
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to apply refresh profile {profile}: {ex.Message}", "Display");
        }
    }

    public static bool TryApplyRefreshViaCcd(int targetHz)
    {
        try
        {
            int err = User32.GetDisplayConfigBufferSizes(User32.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != 0 || pathCount == 0) return false;

            var paths = new User32.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new User32.DISPLAYCONFIG_MODE_INFO[modeCount];

            err = User32.QueryDisplayConfig(User32.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != 0) return false;

            for (int i = 0; i < pathCount; i++)
            {
                paths[i].targetInfo.refreshRate = new User32.DISPLAYCONFIG_RATIONAL
                {
                    Numerator = (uint)targetHz,
                    Denominator = 1
                };

                uint targetModeIdx = paths[i].targetInfo.modeInfoIdx;
                if (targetModeIdx < modeCount && modes[targetModeIdx].infoType == 2 /* Target mode */)
                {
                    modes[targetModeIdx].info.targetMode.targetVideoSignalInfo.vSyncFreq = new User32.DISPLAYCONFIG_RATIONAL
                    {
                        Numerator = (uint)targetHz,
                        Denominator = 1
                    };
                }
            }

            uint flags = User32.SDC_APPLY | User32.SDC_USE_SUPPLIED_DISPLAY_CONFIG | User32.SDC_ALLOW_CHANGES | User32.SDC_SAVE_TO_DATABASE;
            int applyErr = User32.SetDisplayConfig(pathCount, paths, modeCount, modes, flags);
            if (applyErr == 0)
            {
                LogManager.Info($"Successfully applied {targetHz}Hz refresh rate via Windows CCD API", "Display");
                return true;
            }

            LogManager.Debug($"SetDisplayConfig returned {applyErr}; falling back to ChangeDisplaySettingsExW", "Display");
            return false;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"TryApplyRefreshViaCcd exception: {ex.Message}", "Display");
            return false;
        }
    }
}
