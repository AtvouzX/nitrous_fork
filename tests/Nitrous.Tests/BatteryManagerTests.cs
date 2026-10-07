using System.Runtime.InteropServices;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class BatteryManagerTests
{
    [Fact]
    public void SystemBatteryState_SizeIs32()
    {
        int size = Marshal.SizeOf<BatteryManager.SYSTEM_BATTERY_STATE>();
        Assert.Equal(32, size);
    }

    [Fact]
    public void GetBatteryState_DoesNotCrash()
    {
        var state = BatteryManager.GetBatteryState();
        // Just executing it to ensure P/Invoke definitions are valid.
        Assert.True(true);
    }

    [Fact]
    public void GetBatteryHealth_DoesNotCrash()
    {
        var health = BatteryManager.GetBatteryHealth();
        // This might be null if running on a desktop, or non-null on laptop.
        // The main point is to ensure it doesn't throw AccessViolationException or similar marshaling faults.
        Assert.True(true);
    }
}
