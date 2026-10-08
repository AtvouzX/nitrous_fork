using System;
using System.Security;
using Microsoft.Win32;

namespace Nitrous.Managers;

public static class SettingsManager
{
    private const string RegPath = @"Software\Nitrous";

    public static void Save(string key, object value)
    {
        try
        {
            using RegistryKey regKey = Registry.CurrentUser.CreateSubKey(RegPath);
            regKey.SetValue(key, value);

            try
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(Program.SettingsChangedEventName, out var syncEvent))
                {
                    using (syncEvent)
                    {
                        syncEvent.Set();
                    }
                }
            }
            catch { }
        }
        catch (UnauthorizedAccessException) { /* Safe failure on permission denial */ }
        catch (SecurityException) { /* Safe failure on security restriction */ }
        catch (Exception) { }
    }

    public static void Delete(string key)
    {
        try
        {
            using RegistryKey? regKey = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
            regKey?.DeleteValue(key, throwOnMissingValue: false);

            try
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(Program.SettingsChangedEventName, out var syncEvent))
                {
                    using (syncEvent)
                    {
                        syncEvent.Set();
                    }
                }
            }
            catch { }
        }
        catch (UnauthorizedAccessException) { }
        catch (SecurityException) { }
        catch (Exception) { }
    }

    public static T Get<T>(string key, T defaultValue)
    {
        try
        {
            using RegistryKey? regKey = Registry.CurrentUser.OpenSubKey(RegPath);
            object? val = regKey?.GetValue(key);
            if (val != null) return (T)Convert.ChangeType(val, typeof(T))!;
        }
        catch (UnauthorizedAccessException) { }
        catch (SecurityException) { }
        catch (Exception) { }

        return defaultValue;
    }
}
