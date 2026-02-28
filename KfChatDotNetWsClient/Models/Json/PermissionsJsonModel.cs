using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

public class PermissionsJsonModel
{
    [JsonPropertyName("can_view")]
    public required bool CanView { get; set; }
    [JsonPropertyName("can_send")]
    public required bool CanSend { get; set; }
    [JsonPropertyName("can_edit_own")]
    public required bool CanEditOwn { get; set; }
    [JsonPropertyName("can_edit_other")]
    public required bool CanEditOther { get; set; }
    [JsonPropertyName("can_delete_own")]
    public required bool CanDeleteOwn { get; set; }
    [JsonPropertyName("can_delete_other")]
    public required bool CanDeleteOther { get; set; }
    [JsonPropertyName("can_report")]
    public required bool CanReport { get; set; }
    [JsonPropertyName("can_view_deleted")]
    public required bool CanViewDeleted { get; set; }
    [JsonPropertyName("can_undelete")]
    public required bool CanUndelete { get; set; }
    [JsonPropertyName("can_motd")]
    public required bool CanMotd { get; set; }
}