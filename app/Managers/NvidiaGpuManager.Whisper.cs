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
    private bool? _pendingWhisperMode;

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
    /// Returns the currently pending Whisper mode if deferred due to dGPU sleep.
    /// </summary>
    internal bool? PendingWhisperMode => _pendingWhisperMode;

    /// <summary>
    /// Applies WhisperMode 2.0 via NVIDIA NvAPI.
    /// Preserves D3Cold: if dGPU is asleep or uninitialized, records pending state without waking.
    /// Idempotent: skips calls if already in the target state.
    /// </summary>
    public bool SetWhisperMode(bool enable)
    {
        if (!IsGpuAwake())
        {
            _pendingWhisperMode = enable;
            LogManager.Debug($"SetWhisperMode: dGPU is asleep; deferring Whisper mode ({enable})", "NvidiaGpu");
            return false;
        }

        if (!EnsureInitialized() && !IsTestHost())
        {
            _pendingWhisperMode = enable;
            return false;
        }

        if (_lastAppliedWhisperMode == enable)
        {
            return true;
        }

        bool success = SetWhisperModeInternal(enable);
        if (success)
        {
            _lastAppliedWhisperMode = enable;
            _pendingWhisperMode = null;
            LogManager.Info($"Whisper mode set to {(enable ? "ON" : "OFF")}", "NvidiaGpu");
        }
        return success;
    }

    /// <summary>
    /// Flushes any pending whisper mode if the dGPU has awakened.
    /// </summary>
    public void FlushPendingWhisperMode()
    {
        if (_pendingWhisperMode.HasValue && IsGpuAwake())
        {
            SetWhisperMode(_pendingWhisperMode.Value);
        }
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
                _ = setControl(ref ctlMode);

                var ctlFps = new NvToppsJpacControl
                {
                    Version = NvToppsJpacControl.VersionMagic,
                    Field4 = 1,
                    ControlType = 2,
                    Value = 45 // 45 FPS target
                };
                _ = setControl(ref ctlFps);

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
            if (_nvToppsJpacResolved) return _nvToppsJpacSetControl;

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

