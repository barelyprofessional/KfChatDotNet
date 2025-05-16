using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class RainbetBetHistoryModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("value")]
    public required float Value { get; set; }
    [JsonPropertyName("payout")]
    public required float? Payout { get; set; }
    [JsonPropertyName("multiplier")]
    public float? Multiplier { get; set; }
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
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
    public float WageredAmount { get; set; } = 0;
    [JsonPropertyName("public_profile")]
    public int PublicProfile { get; set; } = 0;
    // Null when they have no rank
    [JsonPropertyName("rank")]
    public RainbetBetHistoryUserRankModel? Rank { get; set; }
}

public class RainbetWsBetModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("currencyAmount")]
    public required string CurrencyAmount { get; set; }
    [JsonPropertyName("currency")]
    public required string CurrencyName { get; set; }
    [JsonPropertyName("value")]
    public required string Value { get; set; }
    [JsonPropertyName("payout")]
    public required string Payout { get; set; }
    [JsonPropertyName("currencyPayout")]
    public required string CurrencyPayout { get; set; }
    [JsonPropertyName("multiplier")]
    public required string Multiplier { get; set; }
    [JsonPropertyName("updatedAt")]
    public required DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("user")]
    public required RainbetWsUserModel User { get; set; }
    [JsonPropertyName("game")]
    public required RainbetWsGameModel Game { get; set; }
}

public class RainbetWsUserModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("publicId")]
    public required string PublicId { get; set; }
    [JsonPropertyName("username")]
    // null for private profiles
    public string? Username { get; set; }
    [JsonPropertyName("publicProfile")]
    public required int PublicProfile { get; set; }
}

public class RainbetWsGameModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("url")]
    public required string Url { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

/*
{
  "id": "1a3648a7-e055-49aa-928e-7d9e0c02548a",
  "currencyAmount": "4.0000",
  "currency": "USD",
  "value": "4.0000",
  "currencyPayout": "0.0000",
  "payout": "0.0000",
  "multiplier": "0.0000",
  "updatedAt": "2025-05-16T17:53:49.000Z",
  "user": {
    "id": 784907,
    "publicId": "PIQ230088QABHUGXUT7UH6WUY6PDD473",
    "username": "Gerr...",
    "publicProfile": 1,
    "__betRank__": { "name": "Silver", "level": 1 },
    "rankLevel": { "name": "Silver", "level": 1 }
  },
  "game": {
    "id": 752539,
    "url": "evolution-marble-race",
    "name": "Marble Race",
    "icon": "https://contentdeliverynetwork.cc/i/s3/evolution/MarbleRace.png",
    "iconMini": null,
    "customBanner": null
  },
  "betParameters": null,
  "idString": "1a3648a7-e055-49aa-928e-7d9e0c02548a"
}
*/