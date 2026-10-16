using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nitrous.Enums;
using Nitrous.Managers;
using Nitrous.Mvvm;

namespace Nitrous.Ui;

public partial class DashboardViewModel
{
    public enum GpuAlertLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    private string _applyBtnText = "APPLY";

    public string ApplyBtnText
    {
        get => _applyBtnText;
        set => SetProperty(ref _applyBtnText, value);
    }

    private string _applyBtnColor = "#FF4500";

    public string ApplyBtnColor
    {
        get => _applyBtnColor;
        set => SetProperty(ref _applyBtnColor, value);
    }

    private Visibility _gpuAlertVisibility = Visibility.Collapsed;

    public Visibility GpuAlertVisibility
    {
        get => _gpuAlertVisibility;
        set => SetProperty(ref _gpuAlertVisibility, value);
    }

    private string _gpuAlertTitle = string.Empty;

    public string GpuAlertTitle
    {
        get => _gpuAlertTitle;
        set => SetProperty(ref _gpuAlertTitle, value);
    }

    private string _gpuAlertMessage = string.Empty;

    public string GpuAlertMessage
    {
        get => _gpuAlertMessage;
        set => SetProperty(ref _gpuAlertMessage, value);
    }

    private string _gpuAlertBackground = "#1C1610";

    public string GpuAlertBackground
    {
        get => _gpuAlertBackground;
        set => SetProperty(ref _gpuAlertBackground, value);
    }

    private string _gpuAlertBorder = "#7A4D12";

    public string GpuAlertBorder
    {
        get => _gpuAlertBorder;
        set => SetProperty(ref _gpuAlertBorder, value);
    }

    private string _gpuAlertForeground = "#FF9F0A";

    public string GpuAlertForeground
    {
        get => _gpuAlertForeground;
        set => SetProperty(ref _gpuAlertForeground, value);
    }

    private string _gpuAlertIcon = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M13,17H11V15H13V17M13,13H11V7H13V13Z";

    public string GpuAlertIcon
    {
        get => _gpuAlertIcon;
        set => SetProperty(ref _gpuAlertIcon, value);
    }

    private int _gpuCoreOffset;

    public int GpuCoreOffset
    {
        get => _gpuCoreOffset;
        set => SetProperty(ref _gpuCoreOffset, value);
    }

    private int _gpuMemoryOffset;

    public int GpuMemoryOffset
    {
        get => _gpuMemoryOffset;
        set => SetProperty(ref _gpuMemoryOffset, value);
    }

    public ICommand ApplyGpuClocksCommand { get; private set; } = null!;
    public ICommand ResetGpuClocksCommand { get; private set; } = null!;
    public ICommand DismissGpuAlertCommand { get; private set; } = null!;

    private void InitializeGpuTuning()
    {
        DismissGpuAlertCommand = new RelayCommand(_ => DismissGpuAlert());

        ApplyGpuClocksCommand = new RelayCommand(async _ =>
        {
            try
            {
                var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

                // Save to the active profile configuration
                _gpuManager.SaveCustomProfileOc(currentProfile, GpuCoreOffset, GpuMemoryOffset);

                // 1. Check if running on Battery (DC)
                bool isAcOnline = NvidiaGpuManager.IsAcPowerConnected();
                if (!isAcOnline)
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Warning,
                        "GPU Tuning Deferred (On Battery)",
                        "Discrete GPU overclocking is disabled on battery power to preserve D3Cold sleep and battery life. Your offsets are saved and will automatically apply when connected to AC power.",
                        "M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z");

                    ApplyBtnText = "SAVED";
                    ApplyBtnColor = "#FF9F0A";
                    await Task.Delay(2000);
                    ApplyBtnText = "APPLY";
                    ApplyBtnColor = "#FF4500";
                    return;
                }

                // 2. Check if GPU is asleep in D3Cold (Optimus)
                if (!NvidiaGpuManager.IsGpuAwake())
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Warning,
                        "GPU in D3Cold Sleep",
                        "The discrete GPU is asleep to conserve power. Your offsets are saved and will automatically apply when the GPU wakes up (you can toggle this in Settings).",
                        "M12.3,2A10,10 0 0,0 1.9,14.42C3.12,17.7 6.3,20 10,20A10,10 0 0,0 20,10C20,6.3 17.7,3.12 14.42,1.9C13.72,1.75 13,1.67 12.3,2Z");

                    ApplyBtnText = "SAVED";
                    ApplyBtnColor = "#FF9F0A";
                    await Task.Delay(2000);
                    ApplyBtnText = "APPLY";
                    ApplyBtnColor = "#FF4500";
                    return;
                }

                // 3. Check if GPU / NvAPI is valid (lazily initializes if awake)
                if (!_gpuManager.EnsureInitialized())
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Error,
                        "NVIDIA NvAPI Unavailable",
                        "Could not establish communication with the discrete GPU via NvAPI. Ensure official NVIDIA graphics drivers are running.");

                    ApplyBtnText = "UNAVAILABLE";
                    ApplyBtnColor = "#FF453A";
                    await Task.Delay(2000);
                    ApplyBtnText = "APPLY";
                    ApplyBtnColor = "#FF4500";
                    return;
                }

                // 4. Check if offsets are out of supported range
                if (GpuCoreOffset < _gpuManager.MinCoreOffset || GpuCoreOffset > _gpuManager.MaxCoreOffset ||
                    GpuMemoryOffset < _gpuManager.MinMemoryOffset || GpuMemoryOffset > _gpuManager.MaxMemoryOffset)
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Error,
                        "Offsets Out of Range",
                        $"Clock offsets exceed supported hardware limits (Core: {_gpuManager.MinCoreOffset} to +{_gpuManager.MaxCoreOffset} MHz, Memory: {_gpuManager.MinMemoryOffset} to +{_gpuManager.MaxMemoryOffset} MHz).");

                    ApplyBtnText = "INVALID";
                    ApplyBtnColor = "#FF453A";
                    await Task.Delay(2000);
                    ApplyBtnText = "APPLY";
                    ApplyBtnColor = "#FF4500";
                    return;
                }

                // 5. Apply clocks to physical GPU
                int result = _gpuManager.SetClocks(GpuCoreOffset, GpuMemoryOffset);

                if (result == 1)
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Success,
                        "Clock Offsets Applied",
                        $"Core offset: {(GpuCoreOffset >= 0 ? "+" : "")}{GpuCoreOffset} MHz | Memory offset: {(GpuMemoryOffset >= 0 ? "+" : "")}{GpuMemoryOffset} MHz committed to discrete GPU.");

                    ApplyBtnText = "APPLIED!";
                    ApplyBtnColor = "#34C759";
                }
                else
                {
                    ShowGpuAlert(
                        GpuAlertLevel.Error,
                        "Driver Rejected Offsets",
                        "The NVIDIA driver rejected the requested clock frequencies. The hardware or driver limits were exceeded. Try lower offset values.");

                    ApplyBtnText = "REJECTED";
                    ApplyBtnColor = "#FF453A";
                }

                await Task.Delay(2000);
                ApplyBtnText = "APPLY";
                ApplyBtnColor = "#FF4500";
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to apply GPU clocks: {ex.Message}", "DashboardVM");
                ShowGpuAlert(
                    GpuAlertLevel.Error,
                    "Application Error",
                    $"An unexpected error occurred while applying clock offsets: {ex.Message}");

                ApplyBtnText = "ERROR";
                ApplyBtnColor = "#FF453A";
                await Task.Delay(2000);
                ApplyBtnText = "APPLY";
                ApplyBtnColor = "#FF4500";
            }
        });

        ResetGpuClocksCommand = new RelayCommand(async _ =>
        {
            try
            {
                var currentProfile = (PowerProfile)SettingsManager.Get("LastPowerMode", (int)PowerProfile.Performance);

                GpuCoreOffset = 0;
                GpuMemoryOffset = 0;

                ShowGpuAlert(
                    GpuAlertLevel.Info,
                    "Clocks Reset to Stock",
                    "GPU core and memory offsets have been reverted to factory stock settings (0 MHz).",
                    "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M13,17H11V11H13V17M13,9H11V7H13V9Z");

                // Wipe custom save and load default config
                await _gpuManager.ResetProfileToDefaultsAsync(currentProfile);

                if (_gpuManager.GetClocks(out int c, out int m))
                {
                    GpuCoreOffset = c;
                    GpuMemoryOffset = m;
                }
            }
            catch (Exception ex)
            {
                LogManager.Warn($"Failed to reset GPU clocks: {ex.Message}", "DashboardVM");
            }
        });
    }

    public void ShowGpuAlert(GpuAlertLevel level, string title, string message, string? customIcon = null)
    {
        GpuAlertTitle = title;
        GpuAlertMessage = message;

        switch (level)
        {
            case GpuAlertLevel.Success:
                GpuAlertBackground = "#101C14";
                GpuAlertBorder = "#2A7A3B";
                GpuAlertForeground = "#34C759";
                GpuAlertIcon = customIcon ?? "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M10,17L5,12L6.41,10.59L10,14.17L17.59,6.58L19,8L10,17Z";
                break;
            case GpuAlertLevel.Warning:
                GpuAlertBackground = "#1C1610";
                GpuAlertBorder = "#7A4D12";
                GpuAlertForeground = "#FF9F0A";
                GpuAlertIcon = customIcon ?? "M12,2L1,21H23L12,2M12,6L19.53,19H4.47L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z";
                break;
            case GpuAlertLevel.Error:
                GpuAlertBackground = "#201214";
                GpuAlertBorder = "#7A2024";
                GpuAlertForeground = "#FF453A";
                GpuAlertIcon = customIcon ?? "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M13,17H11V15H13V17M13,13H11V7H13V13Z";
                break;
            case GpuAlertLevel.Info:
            default:
                GpuAlertBackground = "#101620";
                GpuAlertBorder = "#1D4273";
                GpuAlertForeground = "#0A84FF";
                GpuAlertIcon = customIcon ?? "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M13,17H11V11H13V17M13,9H11V7H13V9Z";
                break;
        }

        GpuAlertVisibility = Visibility.Visible;
    }

    public void DismissGpuAlert()
    {
        GpuAlertVisibility = Visibility.Collapsed;
    }


    public async Task AutoApplyGpuClocksAsync()
    {
        try
        {
            await _gpuManager.AutoApplyClocksAsync(ActivePowerProfile);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"AutoApplyGpuClocksAsync error: {ex.Message}", "DashboardVM");
        }
    }
}
