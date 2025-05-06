using System.Text.Json;
using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Settings;

public class Setting(string? value, SettingDbModel dbEntry, bool cached)
{
    public string? Value { get; set; } = value;
    public SettingDbModel DbEntry { get; set; } = dbEntry;
    public bool Cached { get; set; } = cached;

    public T? JsonDeserialize<T>()
    {
        if (Value == null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(Value) ?? default(T);
    }

    public bool ToBoolean()
    {
        return Value is not (null or "null") && Value.Equals("true", StringComparison.CurrentCultureIgnoreCase);
    }
    
    public T ToType<T>()
    {
        return (T)Convert.ChangeType(Value, typeof(T))!;
    }
}