using System.Text.Json.Serialization;

namespace ThreeXplWsClient.Models;

public class GetWebsocketTokenModel
{
    [JsonPropertyName("data")]
    public required string Data { get; set; }
    [JsonPropertyName("context")]
    public GetWebSocketTokenContextModel? Context { get; set; }
}

public class GetWebSocketTokenContextModel
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }
}