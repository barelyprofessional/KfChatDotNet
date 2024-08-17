using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Settings;

public class SettingValue(string? value, SettingDbModel dbEntry, bool cached)
{
    public string? Value { get; set; } = value;
    public SettingDbModel DbEntry { get; set; } = dbEntry;
    public bool Cached { get; set; } = cached;
}