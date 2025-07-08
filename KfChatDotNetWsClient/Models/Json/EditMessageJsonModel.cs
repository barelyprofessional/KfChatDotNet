using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

public class EditMessageJsonModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}