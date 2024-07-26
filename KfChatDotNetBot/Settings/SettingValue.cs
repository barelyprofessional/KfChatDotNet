using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Settings;

public class SettingValue(string? value, SettingDbModel? dbEntry)
{
    public string? Value { get; set; } = value;
    public SettingDbModel? DbEntry { get; set; } = dbEntry;
}