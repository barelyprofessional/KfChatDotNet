using NLog;

namespace KfChatDotNetKickBot.Settings;

public static class Utils
{
    public static List<string> ToList(this SettingValue settingValue, char separator = ',')
    {
        if (settingValue.Value == null) return new List<string>();
        return settingValue.Value.Split(separator).ToList();
    }

    public static Dictionary<string, T> ToKeyValuePairs<T>(this SettingValue settingValue, char delimiter = ',',
        char separator = '=')
    {
        if (settingValue.Value == null)
        {
            return new Dictionary<string, T>();
        }
        return settingValue.Value.Split(delimiter).ToDictionary(kv => kv.Split(separator)[0],
            kv => ValueToType<T>(kv.Split(separator)[1]));
    }

    public static bool ToBoolean(this SettingValue settingValue)
    {
        var logger = LogManager.GetCurrentClassLogger();
        if (settingValue.Value is null or "null")
        {
            return default;
        }
        
        return settingValue.Value.Equals("true", StringComparison.CurrentCultureIgnoreCase);
    }

    public static T ValueToType<T>(string value)
    {
        return (T)Convert.ChangeType(value, typeof(T));
    }
    
    public static T ToType<T>(this SettingValue settingValue)
    {
        return (T)Convert.ChangeType(settingValue.Value, typeof(T));
    }
    
    public static void SafelyRenameFile(string oldName, string newName)
    {
        var logger = LogManager.GetCurrentClassLogger();
        logger.Debug($"Renaming {oldName} to {newName}");
        try
        {
            File.Move(oldName, newName);
        }
        catch (Exception e)
        {
            logger.Error($"Failed to rename {oldName} to {newName}");
            logger.Error(e);
        }
    }
}