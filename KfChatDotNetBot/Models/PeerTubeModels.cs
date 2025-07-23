using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class PeerTubeVideoDataModel
{
    [JsonPropertyName("uuid")]
    public required string Uuid { get; set; }
    [JsonPropertyName("shortUUID")]
    public required string ShortUuid { get; set; }
    [JsonPropertyName("url")]
    public required string Url { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("category")]
    public Dictionary<string, object>? Category { get; set; }
    [JsonPropertyName("isLive")]
    public required bool IsLive { get; set; }
    [JsonPropertyName("account")]
    public required PeerTubeAccountOrChannelModel Account { get; set; }
    [JsonPropertyName("channel")]
    public required PeerTubeAccountOrChannelModel Channel { get; set; }
}

public class PeerTubeAccountOrChannelModel
{
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}