using System.ComponentModel.DataAnnotations.Schema;

namespace KfChatDotNetBot.Models.DbModels;

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
    // Number of seconds to cache in memory, 0 to not cache
    public double CacheDuration { get; set; } = 0;
    // Value type to assist with admin interaction from chat
    public SettingValueType ValueType { get; set; } = SettingValueType.Undefined;

}

public enum SettingValueType
{
    Boolean,
    /// <summary>
    /// This includes values which are only decimals
    /// </summary>
    Text,
    /// <summary>
    /// It's presumed that your array contains text, don't use this if your array contains complex types
    /// You can use the array value type on delimited values or JSON arrays. For JSON, it's presumed to be like ['str']
    /// </summary>
    Array,
    /// <summary>
    /// Basically for JSON blobs that are encoded into settings
    /// </summary>
    Complex,
    /// <summary>
    /// Default value. Should only be set to this for orphaned settings. My suggestion is you don't allow users
    /// to interact with settings with an undefined type
    /// </summary>
    Undefined
}