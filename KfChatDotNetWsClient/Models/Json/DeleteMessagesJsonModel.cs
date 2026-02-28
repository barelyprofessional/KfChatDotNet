using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

public class DeleteMessagesJsonModel
{
    [JsonPropertyName("delete")]
    public required List<string> MessageIdsToDelete { get; set; }
}