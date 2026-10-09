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
            catch (Exception ex)
            {
                LogManager.Debug($"Failed to signal settings change event: {ex.Message}", "Settings");
            }
        }
        catch (UnauthorizedAccessException ex) { LogManager.Warn($"Unauthorized access saving setting '{key}': {ex.Message}", "Settings"); }
        catch (SecurityException ex) { LogManager.Warn($"Security restriction saving setting '{key}': {ex.Message}", "Settings"); }
        catch (Exception ex) { LogManager.Error($"Error saving setting '{key}': {ex.Message}", ex, "Settings"); }
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
            catch (Exception ex)
            {
                LogManager.Debug($"Failed to signal settings change event on delete: {ex.Message}", "Settings");
            }
        }
        catch (UnauthorizedAccessException ex) { LogManager.Warn($"Unauthorized access deleting setting '{key}': {ex.Message}", "Settings"); }
        catch (SecurityException ex) { LogManager.Warn($"Security restriction deleting setting '{key}': {ex.Message}", "Settings"); }
        catch (Exception ex) { LogManager.Error($"Error deleting setting '{key}': {ex.Message}", ex, "Settings"); }
    }

    public static T Get<T>(string key, T defaultValue)
    {
        try
        {
            using RegistryKey? regKey = Registry.CurrentUser.OpenSubKey(RegPath);
            object? val = regKey?.GetValue(key);
            if (val != null) return (T)Convert.ChangeType(val, typeof(T))!;
        }
        catch (UnauthorizedAccessException ex) { LogManager.Debug($"Unauthorized access reading setting '{key}': {ex.Message}", "Settings"); }
        catch (SecurityException ex) { LogManager.Debug($"Security restriction reading setting '{key}': {ex.Message}", "Settings"); }
        catch (Exception ex) { LogManager.Debug($"Error reading setting '{key}': {ex.Message}", "Settings"); }

        return defaultValue;
    }
}
