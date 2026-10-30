using Microsoft.Win32;
using Nitrous.Managers;
using Xunit;

namespace Nitrous.Tests;

public class SettingsManagerTests
{
    private static void Cleanup(string key)
    {
        SettingsManager.Delete(key);
        SettingsManager.InvalidateCache();
    }

    [Fact]
    public void GetInt_ReturnsSavedValue()
    {
        const string key = "T3_Test_GetInt_Basic";
        try
        {
            SettingsManager.Save(key, 42);
            Assert.Equal(42, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReturnsDefaultForMissingKey()
    {
        const string key = "T3_Test_GetInt_Missing";
        try
        {
            SettingsManager.Delete(key);
            SettingsManager.InvalidateCache();
            Assert.Equal(7, SettingsManager.GetInt(key, 7));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReturnsDefaultOnTypeMismatch()
    {
        const string key = "T3_Test_GetInt_Mismatch";
        try
        {
            SettingsManager.Save(key, "not-an-int");
            Assert.Equal(7, SettingsManager.GetInt(key, 7));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ParsesNumericString()
    {
        const string key = "T3_Test_GetInt_NumStr";
        try
        {
            SettingsManager.Save(key, "42");
            Assert.Equal(42, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReadsInRangeLong()
    {
        const string key = "T3_Test_GetInt_FromLong";
        try
        {
            SettingsManager.Save(key, 5L);
            Assert.Equal(5, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReturnsDefaultForOutOfRangeLong()
    {
        const string key = "T3_Test_GetInt_BigLong";
        try
        {
            SettingsManager.Save(key, 9876543210L);
            Assert.Equal(0, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReflectsUpdatedValueAfterResave()
    {
        const string key = "T3_Test_GetInt_Resave";
        try
        {
            SettingsManager.Save(key, 1);
            Assert.Equal(1, SettingsManager.GetInt(key, 0));
            SettingsManager.Save(key, 2);
            Assert.Equal(2, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ReturnsDefaultAfterDelete()
    {
        const string key = "T3_Test_GetInt_Deleted";
        try
        {
            SettingsManager.Save(key, 9);
            Assert.Equal(9, SettingsManager.GetInt(key, 0));
            SettingsManager.Delete(key);
            Assert.Equal(4, SettingsManager.GetInt(key, 4));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_RepeatedReadsReturnStableValue()
    {
        const string key = "T3_Test_GetInt_Stable";
        try
        {
            SettingsManager.Save(key, 3);
            for (int i = 0; i < 25; i++)
            {
                Assert.Equal(3, SettingsManager.GetInt(key, 0));
            }
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetInt_ServesCacheUntilInvalidated()
    {
        const string key = "T3_Test_GetInt_External";
        try
        {
            SettingsManager.Delete(key);
            SettingsManager.InvalidateCache();
            SettingsManager.Save(key, 1);
            Assert.Equal(1, SettingsManager.GetInt(key, 0));

            // External write bypassing Save: no invalidation, like another process.
            using (RegistryKey? reg = Registry.CurrentUser.CreateSubKey(@"Software\Nitrous"))
            {
                reg?.SetValue(key, 2);
            }

            // Cached value still served without registry re-read.
            Assert.Equal(1, SettingsManager.GetInt(key, 0));

            SettingsManager.InvalidateCache();
            Assert.Equal(2, SettingsManager.GetInt(key, 0));
        }
        finally { Cleanup(key); }
    }

    [Fact]
    public void GetLong_RoundTripsTickVersion()
    {
        const string key = "T3_Test_GetLong_Version";
        try
        {
            SettingsManager.Save(key, 9876543210L);
            Assert.Equal(9876543210L, SettingsManager.GetLong(key, 0L));
            Assert.Equal(5L, SettingsManager.GetLong("T3_Test_GetLong_Missing_Xyz", 5L));
        }
        finally { Cleanup(key); SettingsManager.Delete("T3_Test_GetLong_Missing_Xyz"); }
    }

    [Fact]
    public void GetString_RoundTripsFanMode()
    {
        const string key = "T3_Test_GetString_Mode";
        try
        {
            SettingsManager.Save(key, "Medium");
            Assert.Equal("Medium", SettingsManager.GetString(key, "Auto"));
            Assert.Equal("Auto", SettingsManager.GetString("T3_Test_GetString_Missing_Xyz", "Auto"));
        }
        finally { Cleanup(key); SettingsManager.Delete("T3_Test_GetString_Missing_Xyz"); }
    }

    [Fact]
    public void LegacyGet_StillWorksAlongsideTypedGetters()
    {
        const string intKey = "T3_Test_Legacy_Int";
        const string strKey = "T3_Test_Legacy_Str";
        try
        {
            SettingsManager.Save(intKey, 11);
            SettingsManager.Save(strKey, "Medium");

            Assert.Equal(11, SettingsManager.GetInt(intKey, 0));
            Assert.Equal(11, SettingsManager.Get(intKey, 0));
            Assert.Equal("Medium", SettingsManager.GetString(strKey, "Auto"));
            Assert.Equal("Medium", SettingsManager.Get(strKey, "Auto"));
        }
        finally { Cleanup(intKey); Cleanup(strKey); }
    }
}
