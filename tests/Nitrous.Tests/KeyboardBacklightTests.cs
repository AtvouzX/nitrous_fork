using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Nitrous.Enums;
using Nitrous.Helpers;
using Nitrous.Interop;
using Nitrous.Managers;
using Nitrous.Ui.Controls;
using Nitrous.Ui.Views;
using Xunit;
using Application = System.Windows.Application;

namespace Nitrous.Tests;

public class KeyboardBacklightTests : IDisposable
{
    private readonly List<(string Class, string Method, byte[] Buffer)> _wmiBufferCalls = new();
    private readonly List<(string Class, string Method, ulong Input)> _wmiScalarCalls = new();

    public void Dispose()
    {
        AcerWmiManager.SetWmiInvokeForTesting(null);
        AcerWmiManager.SetWmiBufferInvokeForTesting(null);
        AcerWmiManager.SetBacklightLevelForTesting(null);
        AcerWmiManager.SetBacklightTimeoutForTesting(null);
        AcerWmiManager.SetBacklightOffPendingForTesting(null);
    }

    [Theory]
    [InlineData(KeyboardBacklightLevel.Off, 0)]
    [InlineData(KeyboardBacklightLevel.Level1, 100)]
    [InlineData(KeyboardBacklightLevel.Level2, 50)]
    public void EncodeBrightnessPercentage_ReturnsExpectedPercentages(KeyboardBacklightLevel level, int expectedPercent)
    {
        Assert.Equal(expectedPercent, KeyboardBacklightCodec.EncodeBrightnessPercentage(level));
    }

    [Theory]
    [InlineData(0, KeyboardBacklightLevel.Off)]
    [InlineData(1, KeyboardBacklightLevel.Level2)]
    [InlineData(50, KeyboardBacklightLevel.Level2)]
    [InlineData(51, KeyboardBacklightLevel.Level1)]
    [InlineData(100, KeyboardBacklightLevel.Level1)]
    public void DecodeBrightnessLevel_ReturnsMatchingLevel(int percentage, KeyboardBacklightLevel expectedLevel)
    {
        Assert.Equal(expectedLevel, KeyboardBacklightCodec.DecodeBrightnessLevel(percentage));
    }

    [Fact]
    public void EncodeBacklightPayload_Produces16BytePayloadWithBrightnessAtByte2()
    {
        byte[] payloadOff = KeyboardBacklightCodec.EncodeBacklightPayload(KeyboardBacklightLevel.Off);
        Assert.Equal(16, payloadOff.Length);
        Assert.Equal(0x00, payloadOff[0]); // Mode 0 = Static
        Assert.Equal(0x04, payloadOff[1]); // Byte 1: EC header (Probe2 echo)
        Assert.Equal(0, payloadOff[2]);    // Byte 2: Brightness 0%
        Assert.Equal(0, payloadOff[3]);    // Byte 3: 0 (reserved/zeroed per AcerLightingService RVA 0xB27E)
        Assert.Equal(0x04, payloadOff[4]); // Byte 4: EC header (Probe2 echo)
        Assert.Equal(0x03, payloadOff[8]); // Byte 8: Fixed Acer EC marker 0x03
        Assert.Equal(0x01, payloadOff[9]); // Byte 9: Device index 1 (Probe2: 0x01 applies instantly)

        byte[] payloadL1 = KeyboardBacklightCodec.EncodeBacklightPayload(KeyboardBacklightLevel.Level1);
        Assert.Equal(100, payloadL1[2]);   // Level 1 -> dim (100 at Byte 2, EC inverted)

        byte[] payloadL2 = KeyboardBacklightCodec.EncodeBacklightPayload(KeyboardBacklightLevel.Level2);
        Assert.Equal(50, payloadL2[2]);  // Level 2 -> bright (50 at Byte 2, EC inverted)
    }

    [Fact]
    public void EncodeBacklightPayload_PreservesExistingZoneColorsAndRgbMarker()
    {
        byte[] existing = new byte[15];
        existing[5] = 0xAA;
        existing[6] = 0xBB;
        existing[7] = 0xCC;

        byte[] payload = KeyboardBacklightCodec.EncodeBacklightPayload(KeyboardBacklightLevel.Level2, existing, isSingleColor: false);
        Assert.Equal(0xAA, payload[5]);
        Assert.Equal(0xBB, payload[6]);
        Assert.Equal(0xCC, payload[7]);
        Assert.Equal(50, payload[2]);
        Assert.Equal(0x04, payload[1]);
        Assert.Equal(0x04, payload[4]);
        Assert.Equal(0x03, payload[8]);
        Assert.Equal(0x01, payload[9]);
    }

    [Theory]
    [InlineData(0, KeyboardBacklightLevel.Off)]
    [InlineData(100, KeyboardBacklightLevel.Level1)]
    [InlineData(50, KeyboardBacklightLevel.Level2)]
    public void DecodeBacklightLevelFromPayload_ReadsByte2Correctly(byte brightnessByte, KeyboardBacklightLevel expectedLevel)
    {
        byte[] payload = new byte[15];
        payload[2] = brightnessByte;

        Assert.Equal(expectedLevel, KeyboardBacklightCodec.DecodeBacklightLevelFromPayload(payload));
    }

    [Fact]
    public void EncodeGamingProfileTimeout_ProducesExpectedWmiScalar()
    {
        // Per AcerAgentService RVA 0x3E1D6: SetGamingProfile takes 0x01000002ul (enabled) or 0x00000002ul (disabled)
        Assert.Equal(0x01000002ul, KeyboardBacklightCodec.EncodeGamingProfileTimeout(true));
        Assert.Equal(0x00000002ul, KeyboardBacklightCodec.EncodeGamingProfileTimeout(false));
    }

    [Fact]
    public void EncodeApGeActionBacklightPayload_EncodesBrightnessAndTimeoutCorrectly()
    {
        // Per AcerAgentService RVA 0x3E062..0x3E0B0:
        // Low 32 bits: (hotkeyNumber << 8) | 0x00080002
        // High 32 bits: ((timeoutEnabled ? 0x1E00 : 0x0000) | brightnessPercent)
        // EC inverted: L2 = bright (50 = 0x32), L1 = dim (100 = 0x64)
        ulong payload30sL2 = KeyboardBacklightCodec.EncodeApGeActionSetBacklight(KeyboardBacklightLevel.Level2, timeoutEnabled: true, hotkeyNumber: 132);
        Assert.Equal(0x00001E32_00088402ul, payload30sL2);

        ulong payloadAlwaysOnL1 = KeyboardBacklightCodec.EncodeApGeActionSetBacklight(KeyboardBacklightLevel.Level1, timeoutEnabled: false, hotkeyNumber: 132);
        Assert.Equal(0x00000064_00088402ul, payloadAlwaysOnL1);

        ulong payloadOff = KeyboardBacklightCodec.EncodeApGeActionSetBacklight(KeyboardBacklightLevel.Off, timeoutEnabled: true, hotkeyNumber: 132);
        Assert.Equal(0x00001E00_00088402ul, payloadOff);

        Assert.Equal(0x00088401u, KeyboardBacklightCodec.EncodeApGeActionGetBacklight(132));
    }

    [Fact]
    public void EncodeSingleColorLedBehaviorPayload_ProducesExpected16BytePayloads()
    {
        // Probe2 on Nitro ANV14-41: SetGamingLED(byte[16]) with buf[0]=0x08,
        // buf[5]=0x0F (on) or 0x00 (off). Scalar ulong throws InvalidCastException.

        byte[] onL2 = KeyboardBacklightCodec.EncodeSingleColorGamingLedPayload(KeyboardBacklightLevel.Level2);
        Assert.Equal(16, onL2.Length);
        Assert.Equal(0x08, onL2[0]);
        Assert.Equal(0x0F, onL2[5]);

        byte[] onL1 = KeyboardBacklightCodec.EncodeSingleColorGamingLedPayload(KeyboardBacklightLevel.Level1);
        Assert.Equal(16, onL1.Length);
        Assert.Equal(0x08, onL1[0]);
        Assert.Equal(0x0F, onL1[5]);

        byte[] off = KeyboardBacklightCodec.EncodeSingleColorGamingLedPayload(KeyboardBacklightLevel.Off);
        Assert.Equal(16, off.Length);
        Assert.Equal(0x08, off[0]);
        Assert.Equal(0x00, off[5]);
    }

    [Fact]
    public void EncodeTimeoutReport_Produces8BytesWithValidChecksum()
    {
        byte[] reportEnabled = KeyboardBacklightCodec.EncodeTimeoutReport(true);
        Assert.Equal(8, reportEnabled.Length);
        Assert.Equal(0x30, reportEnabled[0]);
        Assert.Equal(0x01, reportEnabled[1]);
        Assert.Equal(0x01, reportEnabled[2]); // Timeout enabled
        Assert.Equal(0x00, reportEnabled[3]);
        Assert.Equal(0xCD, reportEnabled[7]); // Checksum: 0xCE - 1 = 0xCD

        byte[] reportDisabled = KeyboardBacklightCodec.EncodeTimeoutReport(false);
        Assert.Equal(8, reportDisabled.Length);
        Assert.Equal(0x30, reportDisabled[0]);
        Assert.Equal(0x01, reportDisabled[1]);
        Assert.Equal(0x00, reportDisabled[2]); // Timeout disabled (always on)
        Assert.Equal(0xCE, reportDisabled[7]); // Checksum: 0xCE - 0 = 0xCE
    }

    [Fact]
    public void TryDecodeTimeoutReport_ValidatesChecksumAndExtractsState()
    {
        byte[] validOn = [0x30, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0xCD];
        Assert.True(KeyboardBacklightCodec.TryDecodeTimeoutReport(validOn, out bool enabledOn));
        Assert.True(enabledOn);

        byte[] validOff = [0x30, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0xCE];
        Assert.True(KeyboardBacklightCodec.TryDecodeTimeoutReport(validOff, out bool enabledOff));
        Assert.False(enabledOff);

        byte[] badChecksum = [0x30, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0xFF];
        Assert.False(KeyboardBacklightCodec.TryDecodeTimeoutReport(badChecksum, out _));

        byte[] badHeader = [0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0xCD];
        Assert.False(KeyboardBacklightCodec.TryDecodeTimeoutReport(badHeader, out _));
    }

    [Fact]
    public async Task SetBacklightLevelAsync_DispatchesPayloadViaWmiAndSavesSetting()
    {
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            lock (_wmiScalarCalls)
            {
                _wmiScalarCalls.Add((cls, method, input));
            }
            return true;
        });

        AcerWmiManager.SetWmiBufferInvokeForTesting((cls, method, buf) =>
        {
            lock (_wmiBufferCalls)
            {
                _wmiBufferCalls.Add((cls, method, (byte[])buf.Clone()));
            }
            return true;
        });

        await AcerWmiManager.SetBacklightLevelAsync(KeyboardBacklightLevel.Level1);

        Assert.DoesNotContain(_wmiScalarCalls, c => c.Method == "SetGamingLED"); // Buffer only (scalar throws InvalidCastException)
        Assert.NotEmpty(_wmiBufferCalls);

        var ledCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingLED");
        Assert.Equal("AcerGamingFunction", ledCall.Class);
        Assert.Equal(16, ledCall.Buffer.Length);
        Assert.Equal(0x08, ledCall.Buffer[0]);
        Assert.Equal(0x0F, ledCall.Buffer[5]); // Level 1 is on

        var kbCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingKBBacklight");
        Assert.Equal("AcerGamingFunction", kbCall.Class);
        Assert.Equal(16, kbCall.Buffer.Length);
        Assert.Equal(100, kbCall.Buffer[2]); // Level 1 is dim (100 at Byte 2, EC inverted)
        Assert.Equal(0x04, kbCall.Buffer[1]); // EC header (Probe2 echo)
        Assert.Equal(0x04, kbCall.Buffer[4]); // EC header (Probe2 echo)
        Assert.Equal(0x01, kbCall.Buffer[9]); // Device index 1: applies instantly
        Assert.Equal((int)KeyboardBacklightLevel.Level1, SettingsManager.Get("KeyboardBacklightLevel", -1));
    }

    [Fact]
    public void LastInputInfo_StructSize_MatchesWin32SdkDefinition()
    {
        Assert.Equal(8, Marshal.SizeOf<User32.LASTINPUTINFO>());
    }

    [Theory]
    [InlineData(KeyboardBacklightLevel.Off, 0)]
    [InlineData(KeyboardBacklightLevel.Level1, 100)]
    [InlineData(KeyboardBacklightLevel.Level2, 50)]
    public async Task AcerWmiManager_SetBacklightLevelAsync_AltersHardwareAndSaves(KeyboardBacklightLevel targetLevel, int expectedBrightness)
    {
        AcerWmiManager.SetWmiInvokeForTesting((_, _, _) => true);
        AcerWmiManager.SetWmiBufferInvokeForTesting((cls, method, buf) =>
        {
            lock (_wmiBufferCalls)
            {
                _wmiBufferCalls.Add((cls, method, (byte[])buf.Clone()));
            }
            return true;
        });

        await AcerWmiManager.SetBacklightLevelAsync(targetLevel);

        var ledCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingLED");
        Assert.NotNull(ledCall.Buffer);
        Assert.Equal(0x08, ledCall.Buffer[0]);
        Assert.Equal(targetLevel == KeyboardBacklightLevel.Off ? 0x00 : 0x0F, ledCall.Buffer[5]);

        var kbCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingKBBacklight");
        Assert.NotNull(kbCall.Buffer);
        Assert.Equal(expectedBrightness, kbCall.Buffer[2]);
        Assert.Equal(0x04, kbCall.Buffer[1]);
        Assert.Equal(0x04, kbCall.Buffer[4]);
        Assert.Equal(0x01, kbCall.Buffer[9]);
        Assert.Equal((int)targetLevel, SettingsManager.Get("KeyboardBacklightLevel", -1));
    }

    [Fact]
    public async Task AcerWmiManager_ApplyPhysicalBacklightLevelAsync_DoesNotOverwriteSavedSettings()
    {
        SettingsManager.Save("KeyboardBacklightLevel", (int)KeyboardBacklightLevel.Level2);

        AcerWmiManager.SetWmiInvokeForTesting((_, _, _) => true);
        AcerWmiManager.SetWmiBufferInvokeForTesting((cls, method, buf) =>
        {
            lock (_wmiBufferCalls)
            {
                _wmiBufferCalls.Add((cls, method, (byte[])buf.Clone()));
            }
            return true;
        });

        await AcerWmiManager.ApplyPhysicalBacklightLevelAsync(KeyboardBacklightLevel.Off);

        var ledCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingLED");
        Assert.NotNull(ledCall.Buffer);
        Assert.Equal(0x00, ledCall.Buffer[5]); // LED behavior off

        var kbCall = _wmiBufferCalls.Find(c => c.Method == "SetGamingKBBacklight");
        Assert.NotNull(kbCall.Buffer);
        Assert.Equal(0, kbCall.Buffer[2]); // 0% sent to hardware at Byte 2
        // Saved setting must remain Level 2!
        Assert.Equal((int)KeyboardBacklightLevel.Level2, SettingsManager.Get("KeyboardBacklightLevel", -1));
    }

    [Theory]
    [InlineData(true, 0x01000002ul)]
    [InlineData(false, 0x00000002ul)]
    public async Task AcerWmiManager_SetBacklightTimeoutAsync_AltersHardwareTimeoutAndSaves(bool timeoutEnabled, ulong expectedProfileScalar)
    {
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            lock (_wmiScalarCalls)
            {
                _wmiScalarCalls.Add((cls, method, input));
            }
            return true;
        });
        AcerWmiManager.SetWmiBufferInvokeForTesting((_, _, _) => true);

        await AcerWmiManager.SetBacklightTimeoutAsync(timeoutEnabled);

        Assert.Equal(timeoutEnabled, AcerWmiManager.GetBacklightTimeout());
        Assert.Contains(_wmiScalarCalls, c => c.Class == "AcerGamingFunction" && c.Method == "SetGamingProfile" && c.Input == expectedProfileScalar);
    }

    [Fact]
    public void Watchdog_WhenTimeoutEnabledAndL1_DimsAt30Seconds()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        // 25s idle -> Under 30s threshold -> do nothing
        var action1 = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(25));
        Assert.Equal(WatchdogAction.None, action1);
        Assert.False(watchdog.IsDimmed);

        // 30s idle -> Threshold reached -> Dim to Off
        var action2 = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(30));
        Assert.Equal(WatchdogAction.DimToOff, action2);
        Assert.True(watchdog.IsDimmed);

        // 35s idle -> Still idle -> No repetitive action
        var action3 = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(35));
        Assert.Equal(WatchdogAction.None, action3);
        Assert.True(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_WhenTimeoutEnabledAndL2_DimsAt30Seconds()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        // 31s idle -> Dim to Off
        var action = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level2, TimeSpan.FromSeconds(31));
        Assert.Equal(WatchdogAction.DimToOff, action);
        Assert.True(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_WhenUserResumesActivity_RestoresLevel()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        // Dim after 30s
        watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level2, TimeSpan.FromSeconds(30));
        Assert.True(watchdog.IsDimmed);

        // User typed a key -> idle time reset to 1 second -> Restore level
        var restoreAction = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level2, TimeSpan.FromSeconds(1));
        Assert.Equal(WatchdogAction.RestoreLevel, restoreAction);
        Assert.False(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_WhenBrightnessOff_NeverDimsOrRestores()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        var action1 = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(10));
        Assert.Equal(WatchdogAction.None, action1);

        var action2 = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(45));
        Assert.Equal(WatchdogAction.None, action2);
        Assert.False(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_WhenTimeoutDisabled_NeverDimsEvenPast30Seconds()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        var action = watchdog.EvaluateTick(false, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(60));
        Assert.Equal(WatchdogAction.None, action);
        Assert.False(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_WhenTimeoutDisabledWhileDimmed_RestoresImmediately()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        // Dimmed while enabled
        watchdog.EvaluateTick(true, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(35));
        Assert.True(watchdog.IsDimmed);

        // User unchecks 30s timeout toggle in UI
        var action = watchdog.EvaluateTick(false, KeyboardBacklightLevel.Level1, TimeSpan.FromSeconds(40));
        Assert.Equal(WatchdogAction.RestoreLevel, action);
        Assert.False(watchdog.IsDimmed);
    }

    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    new Application();
                }
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    [Fact]
    public void KeyboardPageView_AltersBrightnessLevelsAndTimeout()
    {
        RunInSta(() =>
        {
            AcerWmiManager.SetBacklightLevelForTesting(KeyboardBacklightLevel.Level2);
            AcerWmiManager.SetBacklightTimeoutForTesting(true);
            AcerWmiManager.SetWmiInvokeForTesting((_, _, _) => true);
            AcerWmiManager.SetWmiBufferInvokeForTesting((_, _, _) => true);

            var page = new KeyboardPageView();

            var btnOff = page.FindName("BtnBacklightOff") as ChamferButton;
            var btnL1 = page.FindName("BtnBacklightL1") as ChamferButton;
            var btnL2 = page.FindName("BtnBacklightL2") as ChamferButton;
            var chkTimeout = page.FindName("ChkBacklightTimeout") as System.Windows.Controls.CheckBox;
            var offStatus = page.FindName("TxtBacklightOffStatus") as System.Windows.Controls.TextBlock;

            Assert.NotNull(btnOff);
            Assert.NotNull(btnL1);
            Assert.NotNull(btnL2);
            Assert.NotNull(chkTimeout);
            Assert.NotNull(offStatus);

            // Initially L2 is selected and timeout is checked and enabled
            Assert.Equal(ChamferButtonVariant.Primary, btnL2.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnOff.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnL1.Variant);
            Assert.True(chkTimeout.IsChecked);
            Assert.True(chkTimeout.IsEnabled);

            // Click Off button -> arms EC off: 30s timeout checkbox stays enabled and checked,
            // pending status shows "OFF IN ~30S"
            btnOff.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(ChamferButtonVariant.Primary, btnOff.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnL1.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnL2.Variant);
            Assert.True(chkTimeout.IsEnabled);
            Assert.True(chkTimeout.IsChecked);
            Assert.Equal(Visibility.Visible, offStatus.Visibility);

            // Click L1 button -> re-enables 30s timeout checkbox
            btnL1.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(ChamferButtonVariant.Primary, btnL1.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnOff.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnL2.Variant);
            Assert.True(chkTimeout.IsEnabled);
            Assert.Equal(Visibility.Collapsed, offStatus.Visibility);

            // Click L2 button -> keeps 30s timeout checkbox enabled
            btnL2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(ChamferButtonVariant.Primary, btnL2.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnOff.Variant);
            Assert.Equal(ChamferButtonVariant.Secondary, btnL1.Variant);
            Assert.True(chkTimeout.IsEnabled);

            // Toggle timeout checkbox
            chkTimeout.IsChecked = false;
            Assert.False(chkTimeout.IsChecked);
        });
    }

    [Fact]
    public void BacklightHotkeyWatcher_IsBacklightHotkeyEvent_FiltersOnSecondByte()
    {
        Assert.False(BacklightHotkeyWatcher.IsBacklightHotkeyEvent(null));
        Assert.False(BacklightHotkeyWatcher.IsBacklightHotkeyEvent(Array.Empty<byte>()));
        Assert.False(BacklightHotkeyWatcher.IsBacklightHotkeyEvent(new byte[] { 0x01 }));
        // Live APGeEvent payload: [01 84 08 00 00 00 00 00]
        Assert.True(BacklightHotkeyWatcher.IsBacklightHotkeyEvent(new byte[] { 0x01, 0x84, 0x08, 0x00 }));
        Assert.False(BacklightHotkeyWatcher.IsBacklightHotkeyEvent(new byte[] { 0x01, 0x00, 0x08, 0x00 }));
    }

    [Theory]
    [InlineData(KeyboardBacklightLevel.Off, KeyboardBacklightLevel.Level1)]
    [InlineData(KeyboardBacklightLevel.Level1, KeyboardBacklightLevel.Level2)]
    [InlineData(KeyboardBacklightLevel.Level2, KeyboardBacklightLevel.Off)]
    public void BacklightHotkeyWatcher_AdvanceCycleLevel_FollowsEcCycle(
        KeyboardBacklightLevel current, KeyboardBacklightLevel expected)
    {
        Assert.Equal(expected, BacklightHotkeyWatcher.AdvanceCycleLevel(current));
    }

    [Fact]
    public void Watchdog_PendingOff_ConfirmsOnlyAfterThreshold()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        // Armed but idle below 30s: light is still on, stay pending (no action)
        var pending = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(25), offPending: true);
        Assert.Equal(WatchdogAction.None, pending);
        Assert.False(watchdog.IsDimmed);

        // 30s idle: EC has taken the hardware dark, confirm
        var confirm = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(30), offPending: true);
        Assert.Equal(WatchdogAction.DimToOff, confirm);
        Assert.True(watchdog.IsDimmed);

        // Still idle: no repetitive action
        var steady = watchdog.EvaluateTick(true, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(60), offPending: true);
        Assert.Equal(WatchdogAction.None, steady);
        Assert.True(watchdog.IsDimmed);
    }

    [Fact]
    public void Watchdog_PendingOff_WithoutTimeout_NeverConfirms()
    {
        var watchdog = new KeyboardBacklightWatchdog();

        var action = watchdog.EvaluateTick(false, KeyboardBacklightLevel.Off, TimeSpan.FromSeconds(60), offPending: true);
        Assert.Equal(WatchdogAction.None, action);
        Assert.False(watchdog.IsDimmed);
    }

    [Fact]
    public async Task RequestBacklightOffAsync_ArmsTimeoutThenWritesOff()
    {
        AcerWmiManager.SetBacklightLevelForTesting(KeyboardBacklightLevel.Level1);
        AcerWmiManager.SetBacklightTimeoutForTesting(null);
        AcerWmiManager.SetWmiInvokeForTesting((cls, method, input) =>
        {
            lock (_wmiScalarCalls)
            {
                _wmiScalarCalls.Add((cls, method, input));
            }
            return true;
        });
        AcerWmiManager.SetWmiBufferInvokeForTesting((_, _, _) => true);

        await AcerWmiManager.RequestBacklightOffAsync();

        // Timeout armed first, then Off written
        Assert.True(AcerWmiManager.GetBacklightTimeout());
        Assert.Contains(_wmiScalarCalls, c => c.Class == "AcerGamingFunction" && c.Method == "SetGamingProfile" && c.Input == 0x01000002ul);
        Assert.Equal((int)KeyboardBacklightLevel.Off, SettingsManager.Get("KeyboardBacklightLevel", -1));
        Assert.True(AcerWmiManager.IsBacklightOffPending());
        Assert.Equal((int)KeyboardBacklightLevel.Level1, SettingsManager.Get(AcerWmiManager.BacklightPreviousLevelKey, -1));
    }

    [Fact]
    public async Task CancelBacklightOffAsync_RestoresPreviousLevel()
    {
        SettingsManager.Save(AcerWmiManager.BacklightPreviousLevelKey, (int)KeyboardBacklightLevel.Level2);
        AcerWmiManager.SetBacklightLevelForTesting(KeyboardBacklightLevel.Off);
        AcerWmiManager.SetBacklightTimeoutForTesting(null);
        AcerWmiManager.SetBacklightOffPendingForTesting(true);
        AcerWmiManager.SetWmiInvokeForTesting((_, _, _) => true);
        AcerWmiManager.SetWmiBufferInvokeForTesting((_, _, _) => true);

        await AcerWmiManager.CancelBacklightOffAsync();

        Assert.False(AcerWmiManager.IsBacklightOffPending());
        Assert.False(AcerWmiManager.GetBacklightTimeout());
        Assert.Equal((int)KeyboardBacklightLevel.Level2, SettingsManager.Get("KeyboardBacklightLevel", -1));
    }

    [Fact]
    public void OnHardwareBacklightHotkey_AdvancesTrackedLevelAndClearsPending()
    {
        AcerWmiManager.SetBacklightLevelForTesting(KeyboardBacklightLevel.Level1);
        AcerWmiManager.SetBacklightOffPendingForTesting(true);

        AcerWmiManager.OnHardwareBacklightHotkey();

        Assert.Equal((int)KeyboardBacklightLevel.Level2, SettingsManager.Get("KeyboardBacklightLevel", -1));
        Assert.False(AcerWmiManager.IsBacklightOffPending());
    }
}
