using System;
using System.Collections.Generic;
using System.Security;
using Microsoft.Win32;

namespace Nitrous.Managers;

public static class SettingsManager
{
    private const string RegPath = @"Software\Nitrous";

    // Hot-path cache: telemetry loops read a handful of keys every 1.5-2s.
    // Caches parsed values for CacheTtlMs to avoid per-tick OpenSubKey + ChangeType.
    // Write-through on Save/Delete; InvalidateCache covers cross-process writes
    // (also called from LogManager.SyncSettings on the settings-changed event).
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, CacheEntry> _cache = new();
    private const long CacheTtlMs = 5000;

    private readonly record struct CacheEntry(object? Value, long Ticks);

    public static void Save(string key, object value)
    {
        try
        {
            using RegistryKey regKey = Registry.CurrentUser.CreateSubKey(RegPath);
            regKey.SetValue(key, value);
            SetCached(key, value);

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
            SetCached(key, null);

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

    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    private static void SetCached(string key, object? value)
    {
        lock (_cacheLock)
        {
            _cache[key] = new CacheEntry(value, Environment.TickCount64);
        }
    }

    private static bool TryGetCached(string key, out object? value)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out CacheEntry entry) &&
                Environment.TickCount64 - entry.Ticks <= CacheTtlMs)
            {
                value = entry.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static object? ReadRaw(string key)
    {
        if (TryGetCached(key, out object? cached)) return cached;

        try
        {
            using RegistryKey? regKey = Registry.CurrentUser.OpenSubKey(RegPath);
            object? val = regKey?.GetValue(key);
            SetCached(key, val);
            return val;
        }
        catch (UnauthorizedAccessException ex) { LogManager.Debug($"Unauthorized access reading setting '{key}': {ex.Message}", "Settings"); }
        catch (SecurityException ex) { LogManager.Debug($"Security restriction reading setting '{key}': {ex.Message}", "Settings"); }
        catch (Exception ex) { LogManager.Debug($"Error reading setting '{key}': {ex.Message}", "Settings"); }

        return null;
    }

    // Typed getters: no Convert.ChangeType boxing for hot int/long/string keys.
    public static int GetInt(string key, int defaultValue)
    {
        object? val = ReadRaw(key);
        return val switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static long GetLong(string key, long defaultValue)
    {
        object? val = ReadRaw(key);
        return val switch
        {
            long l => l,
            int i => i,
            string s when long.TryParse(s, out long parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static string GetString(string key, string defaultValue)
    {
        object? val = ReadRaw(key);
        return val switch
        {
            string s => s,
            int i => i.ToString(),
            long l => l.ToString(),
            _ => defaultValue,
        };
    }

    public static T Get<T>(string key, T defaultValue)
    {
        object? val = ReadRaw(key);
        if (val == null) return defaultValue;

        // Fast paths for the most-read hot-loop types, avoiding ChangeType boxing.
        if (typeof(T) == typeof(int))
        {
            if (val is int i) return (T)(object)i;
            if (val is long l && l >= int.MinValue && l <= int.MaxValue) return (T)(object)(int)l;
            if (val is string s && int.TryParse(s, out int p)) return (T)(object)p;
            return defaultValue;
        }
        if (typeof(T) == typeof(long))
        {
            if (val is long l) return (T)(object)l;
            if (val is int i) return (T)(object)(long)i;
            if (val is string s && long.TryParse(s, out long p)) return (T)(object)p;
            return defaultValue;
        }
        if (typeof(T) == typeof(string))
        {
            if (val is string s) return (T)(object)s;
            return (T)(object)(val.ToString() ?? string.Empty);
        }

        try
        {
            return (T)Convert.ChangeType(val, typeof(T))!;
        }
        catch (Exception ex) { LogManager.Debug($"Error reading setting '{key}': {ex.Message}", "Settings"); }

        return defaultValue;
    }
}
