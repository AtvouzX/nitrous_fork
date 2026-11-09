using System;
using System.Threading.Tasks;
using Nitrous.Enums;
using Nitrous.Helpers;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    // Acer EC Fan Behavior Registers
    public const ulong FanBehaviorAuto = 0x41000Ful;
    public const ulong FanBehaviorMax = 0x82000Ful;
    public const ulong FanBehaviorCustom = 0xC3000Ful;

    public static bool ResetFansToAutoSync()
    {
        if (!IsHardwareSupported() && _wmiInvokeOverride == null) return false;
        try
        {
            LogManager.Info("Resetting fan mode to Auto (synchronous failsafe)", "AcerWmi");
            return InvokeWmiSync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorAuto);
        }
        catch (Exception ex)
        {
            LogManager.Error($"Failed resetting fans to Auto: {ex.Message}", ex, "AcerWmi");
            return false;
        }
    }

    /// <summary>
    /// True when the active power mode lets software drive the fans. The EC fan table
    /// owns them in Quiet and Turbo, so manual writes are refused there unless the
    /// Expert Mode setting is on.
    /// </summary>
    public static bool IsFanControlAllowed()
    {
        var mode = (PowerProfile)SettingsManager.GetInt("LastPowerMode", (int)PowerProfile.Performance);
        return HardwareSafetyPolicy.AllowCustomFanWrite(mode, SettingsManager.GetInt("ExpertMode", 0) == 1);
    }

    /// <summary>
    /// Applies fan speeds. Returns false when the active power mode owns the fan table
    /// and the write was refused, so callers never persist speeds that did not land.
    /// Omitted speeds (-1) fall back to the profile percentage; an explicit 0 is a real
    /// request to stop the fan and must reach the EC unchanged.
    /// </summary>
    public static async Task<bool> SetFansAsync(FanProfile profile, int cpuSpeed = -1, int gpuSpeed = -1)
    {
        if (!IsFanControlAllowed())
        {
            LogManager.Warn("Active power mode owns the fan table; ignoring manual fan write", "AcerWmi");
            ResetFansToAutoSync();
            return false;
        }

        LogManager.Info($"Setting fans to {profile} (CPU={cpuSpeed}%, GPU={gpuSpeed}%)", "AcerWmi");
        if (profile == FanProfile.Auto)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorAuto).ConfigureAwait(false);
        }
        else if (profile == FanProfile.Max)
        {
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorMax).ConfigureAwait(false);
        }
        else // Custom (Quiet/Medium or explicit speeds)
        {
            int cpu = Math.Clamp(cpuSpeed, 0, 100);
            int gpu = Math.Clamp(gpuSpeed, 0, 100);
            if (cpuSpeed < 0 && gpuSpeed < 0)
            {
                int unified = Math.Clamp((int)profile, 0, 100);
                cpu = unified;
                gpu = unified;
            }
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanBehavior", FanBehaviorCustom).ConfigureAwait(false);
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 1ul | ((ulong)cpu << 8)).ConfigureAwait(false);
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 2ul | ((ulong)gpu << 8)).ConfigureAwait(false);
            await InvokeWmiAsync("AcerGamingFunction", "SetGamingFanSpeed", 4ul | ((ulong)gpu << 8)).ConfigureAwait(false);
        }

        return true;
    }
}
