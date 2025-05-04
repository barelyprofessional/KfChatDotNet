using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class BetBoltBetPayloadModel
{
    // I've always seen this sent but never know if it'll be null for privacy at some point
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    [JsonPropertyName("game_code")]
    public string? GameCode { get; set; }
    [JsonPropertyName("game_name")]
    public required string GameName { get; set; }
    [JsonPropertyName("rank")]
    public string? Rank { get; set; }
    [JsonPropertyName("time")]
    public required DateTimeOffset Time { get; set; }
    [JsonPropertyName("crypto_code")]
    public required string CryptoCode { get; set; }
    [JsonPropertyName("bet_amount_fiat")]
    public required string BetAmountFiat { get; set; }
    [JsonPropertyName("bet_amount_crypto")]
    public required string BetAmountCrypto { get; set; }
    [JsonPropertyName("win_amount_fiat")]
    // Negatives for losses
    public required string WinAmountFiat { get; set; }
    [JsonPropertyName("win_amount_crypto")]
    public required string WinAmountCrypto { get; set; }
    [JsonPropertyName("multiplier")]
    // null on losses
    public string? Multiplier { get; set; }
    [JsonPropertyName("category_icon")]
    public string? CategoryIcon { get; set; }
    [JsonPropertyName("types")]
    public List<string>? Types { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("topic")]
    public required string Topic { get; set; }
}

public class BetBoltBetModel
{
    // I won't pass through bets with a null username as there's no way to tie it to Austin
    public required string Username { get; set; }
    public required string GameName { get; set; }
    public required DateTimeOffset Time { get; set; }
    public required string Crypto { get; set; }
    public required double BetAmountFiat { get; set; }
    public required double BetAmountCrypto { get; set; }
    // Negative if it's a loss
    public required double WinAmountFiat { get; set; }
    public required double WinAmountCrypto { get; set; }
    public required double Multiplier { get; set; }
    // Eh never know when you'll need it
    public required BetBoltBetPayloadModel Payload { get; set; }
}