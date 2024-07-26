using KfChatDotNetBot.Models.DbModels;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot.Settings;

public static class Helpers
{
    public static async Task<SettingValue> GetValue(string key, bool caseInsensitive = false)
    {
        var logger = LogManager.GetCurrentClassLogger();
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

        if (setting.Value == "null")
        {
            logger.Debug($"{key}'s value is null so returning SettingValue(null)");
            return new SettingValue(null, null);
        }
        
        logger.Debug($"Returning '{setting.Value}' as {typeof(SettingValue)}");
        return new SettingValue(setting.Value, setting);
    }

    public static async Task<Dictionary<string, SettingValue>> GetMultipleValues(string[] keys, bool caseInsensitive = false)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Trace($"Getting values for keys {string.Join(", ", keys)}");

        Dictionary<string, SettingValue> values = new Dictionary<string, SettingValue>();
        foreach (var key in keys)
        {
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
                logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
                throw new KeyNotFoundException();
            }

            if (setting.Value == "null")
            {
                logger.Debug($"{key}'s value is null so returning SettingValue(null)");
                values.Add(key, new SettingValue(null, null));
                continue;
            }
            values.Add(key, new SettingValue(setting.Value, setting));
        }

        return values;
    }

    public static async Task SetValue(string key, object? value)
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
    }

    public static async Task SetValueAsList<T>(string key, List<T> values, char separator = ',')
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        List<string> stringValues = values.Select(val => (string)Convert.ChangeType(val, TypeCode.String)).ToList();
        string joinedValue = string.Join(separator, stringValues);
        logger.Debug($"Setting {key} to {joinedValue}");

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
            throw new KeyNotFoundException();
        }

        setting.Value = joinedValue;
        await db.SaveChangesAsync();
    }

    public static async Task SetValueAsKeyValuePairs<T>(string key, Dictionary<string, T> data, char delimiter = ',',
        char separator = '=')
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        logger.Debug($"Building data for {key}");
        var value = data.Keys.Aggregate(string.Empty,
            (current, dictKey) => current + $"{dictKey}{separator}{data[dictKey]}{delimiter}");

        // Remove trailing delimiters that would be leftover as it doesn't account for whether it's the last key
        value = value.TrimEnd(delimiter);
        logger.Debug($"Setting {key} to {value}");

        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null)
        {
            logger.Debug($"{key} does not exist, throwing KeyNotFoundException()");
            throw new KeyNotFoundException();
        }

        setting.Value = value;
        await db.SaveChangesAsync();
    }

    public static async Task SetValueAsBoolean(string key, bool value)
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
    }
}