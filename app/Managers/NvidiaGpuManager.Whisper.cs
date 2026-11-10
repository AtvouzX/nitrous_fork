using System;
using System.Runtime.InteropServices;
using Nitrous.Enums;

namespace Nitrous.Managers;

public partial class NvidiaGpuManager
{
    private static NativeNvapi.NvToppsJpacSetControlDelegate? _nvToppsJpacSetControl;
    private static bool _nvToppsJpacResolved;
    private static readonly object _whisperLock = new();

    private bool? _lastAppliedWhisperMode;

    /// <summary>Settings key holding the last confirmed NVAPI Whisper state (cross-process).</summary>
    public const string WhisperStateKey = "WhisperModeState";

    public const int WhisperStateOff = 0;
    public const int WhisperStateOn = 1;
    public const int WhisperStateUnavailable = 3;

    private static bool? _isGpuAwakeForTesting;

    internal static void SetGpuAwakeForTesting(bool? isAwake) => _isGpuAwakeForTesting = isAwake;

    /// <summary>
    /// Test seam to inject a custom delegate or inspect calls.
    /// </summary>
    internal static void SetWhisperDelegateForTesting(NativeNvapi.NvToppsJpacSetControlDelegate? testDelegate)
    {
        lock (_whisperLock)
        {
            _nvToppsJpacSetControl = testDelegate;
            _nvToppsJpacResolved = testDelegate != null;
        }
    }

    /// <summary>
    /// Pure check: Quiet and Eco activate WhisperMode 2.0.
    /// </summary>
    public static bool IsWhisperProfile(PowerProfile profile) =>
        profile is PowerProfile.Quiet or PowerProfile.Eco;

    /// <summary>
    /// Applies WhisperMode 2.0 via NVIDIA NvAPI.
    /// Whisper is a driver preference, not a clock write, so it is allowed to wake a
    /// D3Cold dGPU instead of deferring while the GPU sleeps.
    /// Idempotent: skips calls if the same mode was already accepted.
    /// </summary>
    public bool SetWhisperMode(bool enable)
    {
        if (_lastAppliedWhisperMode == enable)
        {
            return true;
        }

        if (!EnsureInitializedForWhisper())
        {
            SettingsManager.Save(WhisperStateKey, WhisperStateUnavailable);
            LogManager.Debug($"SetWhisperMode: NvAPI unavailable; Whisper mode ({enable}) not applied", "NvidiaGpu");
            return false;
        }

        bool success = SetWhisperModeInternal(enable);
        if (success)
        {
            _lastAppliedWhisperMode = enable;
            SettingsManager.Save(WhisperStateKey, enable ? WhisperStateOn : WhisperStateOff);
            LogManager.Info($"Whisper mode set to {(enable ? "ON" : "OFF")}", "NvidiaGpu");
        }
        else
        {
            SettingsManager.Save(WhisperStateKey, WhisperStateUnavailable);
        }
        return success;
    }

    /// <summary>
    /// Binds the NVIDIA driver for WhisperMode, waking the dGPU when needed.
    /// Waking is an accepted cost for this feature, so it never leaves a deferred state.
    /// </summary>
    private bool EnsureInitializedForWhisper()
    {
        if (EnsureInitialized()) return true;
        if (IsTestHost()) return true;

        InitializeNvAPI(allowWake: true);
        return _internalGpu != null;
    }

    private static bool SetWhisperModeInternal(bool enable)
    {
        var setControl = GetSetControlDelegate();
        if (setControl == null)
        {
            LogManager.Debug("SetWhisperMode: NvAPI_NvToppsJpacSetControl not available", "NvidiaGpu");
            return false;
        }

        try
        {
            if (enable)
            {
                var ctlEnable = new NvToppsJpacControl
                {
                    Version = NvToppsJpacControl.VersionMagic,
                    Field4 = 1,
                    ControlType = 0,
                    Value = 1
                };
                int ret = setControl(ref ctlEnable);
                if (ret != 0)
                {
                    LogManager.Warn($"SetWhisperMode enable failed, ret={ret}", "NvidiaGpu");
                    return false;
                }

                var ctlMode = new NvToppsJpacControl
                {
                    Version = NvToppsJpacControl.VersionMagic,
                    Field4 = 1,
                    ControlType = 1,
                    Value = 1 // QUIET mode
                };
                int modeRet = setControl(ref ctlMode);

                var ctlFps = new NvToppsJpacControl
                {
                    Version = NvToppsJpacControl.VersionMagic,
                    Field4 = 1,
                    ControlType = 2,
                    Value = 45 // 45 FPS target
                };
                int fpsRet = setControl(ref ctlFps);

                // The chip claims ON, so all three controls must land. A silent partial
                // apply would report Whisper as active while power/FPS caps did not stick.
                if (modeRet != 0 || fpsRet != 0)
                {
                    LogManager.Warn(
                        $"SetWhisperMode partial apply: quiet mode ret={modeRet}, fps cap ret={fpsRet}", "NvidiaGpu");
                    return false;
                }

                return true;
            }
            else
            {
                var ctlDisable = new NvToppsJpacControl
                {
                    Version = NvToppsJpacControl.VersionMagic,
                    Field4 = 1,
                    ControlType = 0,
                    Value = 0
                };
                int ret = setControl(ref ctlDisable);
                return ret == 0;
            }
        }
        catch (Exception ex)
        {
            LogManager.Debug($"SetWhisperMode error: {ex.Message}", "NvidiaGpu");
            return false;
        }
    }

    private static NativeNvapi.NvToppsJpacSetControlDelegate? GetSetControlDelegate()
    {
        lock (_whisperLock)
        {
            // A failed lookup is not final: the first attempt can run before NVAPI is
            // initialized, so null stays retryable instead of caching a dead end.
            if (_nvToppsJpacResolved && _nvToppsJpacSetControl != null) return _nvToppsJpacSetControl;

            try
            {
                IntPtr ptr = NativeNvapi.QueryInterface(NativeNvapi.NvToppsJpacSetControlId);
                if (ptr != IntPtr.Zero)
                {
                    _nvToppsJpacSetControl = Marshal.GetDelegateForFunctionPointer<NativeNvapi.NvToppsJpacSetControlDelegate>(ptr);
                }
            }
            catch (Exception ex)
            {
                LogManager.Debug($"Resolve NvToppsJpacSetControl failed: {ex.Message}", "NvidiaGpu");
            }
            finally
            {
                _nvToppsJpacResolved = true;
            }

            return _nvToppsJpacSetControl;
        }
    }
}

