namespace KfChatDotNetKickBot.Models;

public class BuiltInSettingsModel
{
    // Model here largely maps to what's in SettingDbModel, the idea is that there's a set of built-in settings that get
    // populated when migrating old JSON configs and updated on start if there's a schema change (e.g. regex changed)
    public required string Key { get; set; }
    public required string Regex { get; set; }
    public required string Description { get; set; }
    public string? Default { get; set; }
    public required bool IsSecret { get; set; }

}