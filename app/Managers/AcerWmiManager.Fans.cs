using System;
using System.Threading.Tasks;
using Nitrous.Enums;

namespace Nitrous.Managers;

public static partial class AcerWmiManager
{
    // Acer EC Fan Behavior Registers
    public const ulong FanBehaviorAuto = 0x41000Ful;
    public const ulong FanBehaviorMax = 0x82000Ful;
    public const ulong FanBehaviorCustom = 0xC3000Ful;

    public static bool ResetFansToAutoSync()
    {
        if (!IsHardwareSupported()) return false;
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

    public static async Task SetFansAsync(FanProfile profile, int cpuSpeed = 0, int gpuSpeed = 0)
    {
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
            if (cpu <= 0 && gpu <= 0)
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
    }
}
