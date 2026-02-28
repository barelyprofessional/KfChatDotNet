using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

public class EditMessageJsonModel
{
    [JsonPropertyName("uuid")]
    public required string Uuid { get; set; }
    
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}