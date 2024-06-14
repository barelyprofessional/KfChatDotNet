using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

public class EditMessageJsonModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; }
}