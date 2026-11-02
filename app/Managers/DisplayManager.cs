using System;
using System.Collections.Generic;
using System.Linq;
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

    public static int GetCurrentRefreshRate()
    {
        try
        {
            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            return User32.EnumDisplaySettingsW(null, User32.ENUM_CURRENT_SETTINGS, ref mode)
                ? mode.dmDisplayFrequency
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static IReadOnlyList<int> GetSupportedRefreshRates()
    {
        var rates = new SortedSet<int>();
        try
        {
            var current = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            if (!User32.EnumDisplaySettingsW(null, User32.ENUM_CURRENT_SETTINGS, ref current))
                return Array.Empty<int>();

            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            for (int i = 0; User32.EnumDisplaySettingsW(null, i, ref mode); i++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth &&
                    mode.dmPelsHeight == current.dmPelsHeight &&
                    mode.dmDisplayFrequency > 0)
                {
                    rates.Add(mode.dmDisplayFrequency);
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Failed to enumerate supported refresh rates: {ex.Message}", "Display");
        }

        return rates.ToArray();
    }

    public static bool TryFindMode(int width, int height, int targetHz, out User32.DEVMODEW mode)
    {
        mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
        var candidate = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };

        for (int i = 0; User32.EnumDisplaySettingsW(null, i, ref candidate); i++)
        {
            if (candidate.dmPelsWidth == width &&
                candidate.dmPelsHeight == height &&
                candidate.dmDisplayFrequency == targetHz)
            {
                mode = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsRefreshSupported(int width, int height, int targetHz) =>
        TryFindMode(width, height, targetHz, out _);

    public static int ResolveTargetRefreshRate(RefreshProfile profile, bool isAcPower, int maxHz) =>
        profile switch
        {
            RefreshProfile.Hz60 => 60,
            RefreshProfile.MaxHz => maxHz,
            RefreshProfile.Auto => isAcPower ? maxHz : 60,
            _ => 60
        };

    public static void ApplyRefreshProfile(RefreshProfile profile, bool isAcPower)
    {
        try
        {
            int maxHz = GetPrimaryMaxRefreshRate();
            int targetHz = ResolveTargetRefreshRate(profile, isAcPower, maxHz);

            var mode = new User32.DEVMODEW { dmSize = (short)Marshal.SizeOf<User32.DEVMODEW>() };
            if (!User32.EnumDisplaySettingsW(null, User32.ENUM_CURRENT_SETTINGS, ref mode))
            {
                LogManager.Warn("Display: could not read current display mode.", "Display");
                return;
            }

            if (mode.dmDisplayFrequency == targetHz) return;

            if (!TryFindMode(mode.dmPelsWidth, mode.dmPelsHeight, targetHz, out var driverMode))
            {
                LogManager.Warn(
                    $"Display: {targetHz}Hz is not supported at {mode.dmPelsWidth}x{mode.dmPelsHeight}; leaving current mode unchanged.",
                    "Display");
                return;
            }

            LogManager.Info(
                $"Switching display refresh rate from {mode.dmDisplayFrequency}Hz to {targetHz}Hz (Profile: {profile}, AC: {isAcPower})",
                "Display");

            // 1. Primary method (fast/seamless): ChangeDisplaySettingsExW with DM_DISPLAYFREQUENCY only.
            mode.dmFields = User32.DM_DISPLAYFREQUENCY;
            mode.dmDisplayFrequency = targetHz;

            int ret = User32.ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, User32.CDS_UPDATEREGISTRY,
                IntPtr.Zero);
            if (ret != User32.DISP_CHANGE_SUCCESSFUL && !string.IsNullOrEmpty(mode.dmDeviceName))
            {
                ret = User32.ChangeDisplaySettingsExW(mode.dmDeviceName, ref mode, IntPtr.Zero,
                    User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
            }

            if (ret == User32.DISP_CHANGE_SUCCESSFUL)
            {
                return;
            }

            LogManager.Warn($"ChangeDisplaySettingsExW in-place returned {ret}; trying driver mode entry", "Display");

            // 2. Secondary fallback: Apply driver mode entry whole
            int driverModeRet = User32.ChangeDisplaySettingsExW(null, ref driverMode, IntPtr.Zero, User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (driverModeRet == User32.DISP_CHANGE_SUCCESSFUL)
            {
                return;
            }

            LogManager.Warn($"ChangeDisplaySettingsExW driver mode returned {driverModeRet}; attempting CCD fallback", "Display");

            // 3. Tertiary fallback: Windows CCD API
            if (TryApplyRefreshViaCcd(targetHz))
            {
                return;
            }

            LogManager.Warn($"All refresh rate application methods failed for {targetHz}Hz", "Display");
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
            int err = User32.GetDisplayConfigBufferSizes(User32.QDC_ONLY_ACTIVE_PATHS, out uint pathCount,
                out uint modeCount);
            if (err != 0 || pathCount == 0) return false;

            var paths = new User32.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new User32.DISPLAYCONFIG_MODE_INFO[modeCount];

            err = User32.QueryDisplayConfig(User32.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes,
                IntPtr.Zero);
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
                    modes[targetModeIdx].info.targetMode.targetVideoSignalInfo.vSyncFreq =
                        new User32.DISPLAYCONFIG_RATIONAL
                        {
                            Numerator = (uint)targetHz,
                            Denominator = 1
                        };
                }
            }

            uint flags = User32.SDC_APPLY | User32.SDC_USE_SUPPLIED_DISPLAY_CONFIG | User32.SDC_ALLOW_CHANGES |
                         User32.SDC_SAVE_TO_DATABASE;
            int applyErr = User32.SetDisplayConfig(pathCount, paths, modeCount, modes, flags);
            if (applyErr == 0)
            {
                LogManager.Info($"Successfully applied {targetHz}Hz refresh rate via Windows CCD API", "Display");
                return true;
            }

            LogManager.Debug($"SetDisplayConfig returned {applyErr}; falling back to ChangeDisplaySettingsExW",
                "Display");
            return false;
        }
        catch (Exception ex)
        {
            LogManager.Debug($"TryApplyRefreshViaCcd exception: {ex.Message}", "Display");
            return false;
        }
    }
}
