using System.Text.Json.Serialization;

namespace ThreeXplWsClient.Models;

public class ConnectRequestModel
{
    [JsonPropertyName("connect")]
    public required ConnectRequestTokenModel Connect { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; } = 1;
}

public class ConnectRequestTokenModel
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }
}

public class SubscribeRequestModel
{
    [JsonPropertyName("subscribe")]
    public required SubscribeRequestChannelModel Subscribe { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; } = 2;
}

public class SubscribeRequestChannelModel
{
    [JsonPropertyName("channel")]
    public required string Channel { get; set; }
}