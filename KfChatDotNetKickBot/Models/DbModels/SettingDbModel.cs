using System.ComponentModel.DataAnnotations.Schema;

namespace KfChatDotNetKickBot.Models.DbModels;

public class SettingDbModel
{
    public int Id { get; set; }
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public required string Key { get; set; }
    public string? Value { get; set; }
    // For validation
    public required string Regex { get; set; } = @"\S+";
    // Friendly descriptor for the setting, e.g. "BossmanJack's howl.gg ID"
    public required string Description { get; set; }
    // Default to use when constructing the setting and nothing is supplied
    public string? Default { get; set; } = null;
    // Prevents the value from being revealed to Sneedchat when queried by an admin
    public bool IsSecret { get; set; } = false;
}