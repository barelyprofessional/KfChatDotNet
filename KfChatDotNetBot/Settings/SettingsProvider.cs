using KfChatDotNetBot.Models.DbModels;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Caching;
using System.Text.Json;
using NLog;

namespace KfChatDotNetBot.Settings;

public static class SettingsProvider
{
    public static async Task<Setting> GetValueAsync(string key, bool caseInsensitive = false, bool bypassCache = false)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var cache = MemoryCache.Default;
        if (!bypassCache && cache.Contains(key))
        {
            var cachedSetting = cache.Get(key) as SettingDbModel;
            var value = cachedSetting.Value;
            if (cachedSetting.Value == "null") value = null;
            return new Setting(value, cachedSetting, true);
        }
        await using var db = new ApplicationDbContext();
        logger.Trace($"Retrieving value for {key}");

        SettingDbModel? setting;
        if (caseInsensitive)
        {
            // String comparison doesn't work on EF core if I recall correctly
#pragma warning disable CA1862
            setting = await db.Settings.FirstOrDefaultAsync(s => s.Key.ToLower() == key.ToLower());
#pragma warning restore CA1862
        }
        else
        {
            setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        }
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException");
            throw new KeyNotFoundException($"{key} does not exist");
        }

        cache.Set(key, setting, new CacheItemPolicy {AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(setting.CacheDuration)});

        if (setting.Value == "null")
        {
            logger.Debug($"{key}'s value is null so returning SettingValue(null)");
            return new Setting(null, setting, false);
        }

        if (setting.IsSecret)
        {
            logger.Info($"Cache Miss! Returning secret of length '{setting.Value?.Length}' for {key}");

        }
        else
        {
            logger.Info($"Cache Miss! Returning '{setting.Value}' for {key}");

        }
        return new Setting(setting.Value, setting, false);
    }

    public static async Task<Dictionary<string, Setting>> GetMultipleValuesAsync(string[] keys, bool caseInsensitive = false, bool bypassCache = false)
    {
        var logger = LogManager.GetCurrentClassLogger();
        logger.Trace($"Getting values for keys {string.Join(", ", keys)}");

        var values = new Dictionary<string, Setting>();
        foreach (var key in keys)
        {
            values.Add(key, await GetValueAsync(key, caseInsensitive, bypassCache));
        }

        return values;
    }

    public static async Task SetValueAsync(string key, object? value)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        string stringValue;
        if (value == null)
        {
            stringValue = "null";
        }
        else if (value is string)
        {
            stringValue = (string)value;
        }
        else
        {
            stringValue = (string)Convert.ChangeType(value, TypeCode.String);
        }
        logger.Debug($"Setting {key} to {stringValue}");

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
            throw new KeyNotFoundException();
        }

        setting.Value = stringValue;
        await db.SaveChangesAsync();
        var cache = MemoryCache.Default;
        if (cache.Contains(key)) cache.Remove(key);
    }

    public static async Task SetValueAsJsonObjectAsync<T>(string key, T data)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Debug($"Building data for {key}");
        var value = JsonSerializer.Serialize(data);
        
        logger.Debug($"Setting {key} to {value}");

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
            throw new KeyNotFoundException();
        }

        setting.Value = value;
        await db.SaveChangesAsync();
        var cache = MemoryCache.Default;
        if (cache.Contains(key)) cache.Remove(key);
    }

    public static async Task SetValueAsBooleanAsync(string key, bool value)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Debug($"Setting {key} to {value}");

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
            throw new KeyNotFoundException();
        }

        setting.Value = value ? "true" : "false";

        await db.SaveChangesAsync();
        var cache = MemoryCache.Default;
        if (cache.Contains(key)) cache.Remove(key);
    }
}