using System.Text.Json;
using System.Text.Json.Serialization;

namespace KfChatDotNetKickBot.Models;

public class JackpotWsPacketModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("type")]
    public required string Type { get; set; }
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}

public class JackpotWsBetPayloadModel
{
    [JsonPropertyName("createdAt")]
    public required long CreatedAt { get; set; }
    [JsonPropertyName("roundId")]
    public required string RoundId { get; set; }
    [JsonPropertyName("gameName")]
    public required string GameName { get; set; }
    [JsonPropertyName("gameSlug")]
    public required string GameSlug { get; set; }
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("wager")]
    public required float Wager { get; set; }
    [JsonPropertyName("payout")]
    public required float Payout { get; set; }
    [JsonPropertyName("multiplier")]
    public required float Multiplier { get; set; }
    [JsonPropertyName("user")]
    public required string User { get; set; }
}

public class JackpotQuickviewModel
{
    [JsonPropertyName("createdAt")]
    public required long CreatedAt { get; set; }
    [JsonPropertyName("userId")]
    public required string UserId { get; set; }
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("isPrivate")]
    public bool IsPrivate { get; set; }
    [JsonPropertyName("role")]
    public required string Role { get; set; }
    [JsonPropertyName("rankId")]
    public required int RankId { get; set; }
    [JsonPropertyName("rank")]
    public required string Rank { get; set; }
    [JsonPropertyName("rankProgress")]
    public required float RankProgress { get; set; }
    [JsonPropertyName("wagered")]
    public required Dictionary<string, float> Wagered { get; set; }
    [JsonPropertyName("bets")]
    public required Dictionary<string, int> Bets { get; set; }
    [JsonPropertyName("wins")]
    public required Dictionary<string, int> Wins { get; set; }
}