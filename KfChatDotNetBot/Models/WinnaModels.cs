using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class WinnaBetModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("userName")]
    public required string Username { get; set; }
    [JsonPropertyName("isVip")]
    public required bool IsVip { get; set; }
    [JsonPropertyName("tier")]
    public string? Tier { get; set; }
    [JsonPropertyName("level")]
    public required int Level { get; set; }
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("multiplier")]
    public required float Multiplier { get; set; }
    [JsonPropertyName("betAmount")]
    public required float BetAmount { get; set; }
    [JsonPropertyName("payout")]
    public required float Payout { get; set; }
    [JsonPropertyName("gameName")]
    public required string GameName { get; set; }
    [JsonPropertyName("amounts")]
    public required Dictionary<string, WinnaCurrencyModel> Amounts { get; set; }
}

public class WinnaCurrencyModel
{
    [JsonPropertyName("betAmount")]
    public required float BetAmount { get; set; }
    [JsonPropertyName("payout")]
    public required float Payout { get; set; }
}