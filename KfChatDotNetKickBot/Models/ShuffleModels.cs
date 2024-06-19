using System.Text.Json.Serialization;

namespace KfChatDotNetKickBot.Models;

public class ShuffleLatestBetModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    [JsonPropertyName("vipLevel")]
    public required string VipLevel { get; set; }
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("amount")]
    public required string Amount { get; set; }
    [JsonPropertyName("payout")]
    public required string Payout { get; set; }
    [JsonPropertyName("multiplier")]
    public required string Multiplier { get; set; }
    [JsonPropertyName("gameName")]
    public required string GameName { get; set; }
    [JsonPropertyName("gameCategory")]
    public required string GameCategory { get; set; }
    [JsonPropertyName("gameSlug")]
    public required string GameSlug { get; set; }
}