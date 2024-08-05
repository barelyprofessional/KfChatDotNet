using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class RainbetBetHistoryModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("value")]
    public required float Value { get; set; }
    [JsonPropertyName("payout")]
    public required float Payout { get; set; }
    [JsonPropertyName("multiplier")]
    public required float Multiplier { get; set; }
    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("user")]
    public required RainbetBetHistoryUserModel User { get; set; }
    [JsonPropertyName("game")]
    public required RainbetBetHistoryGameModel Game { get; set; }
}

public class RainbetBetHistoryGameModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    // It's actually a slug
    [JsonPropertyName("url")]
    public required string Url { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

public class RainbetBetHistoryUserRankModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("level")]
    public required int Level { get; set; }
    [JsonPropertyName("threshold")]
    public required int Threshold { get; set; }
}

public class RainbetBetHistoryUserModel
{
    // Can still uniquely identify users even if they're private. Bossman is ID 50
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    // Set to null on private-profiles
    [JsonPropertyName("publicId")]
    public string? PublicId { get; set; }
    // Set to null on private profiles
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    [JsonPropertyName("wageredAmount")]
    public required float WageredAmount { get; set; }
    [JsonPropertyName("public_profile")]
    public required int PublicProfile { get; set; }
    // Null when they have no rank
    [JsonPropertyName("rank")]
    public RainbetBetHistoryUserRankModel? Rank { get; set; }
}