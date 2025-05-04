namespace KfChatDotNetBot.Models;

public class BetBoltBetPayloadModel
{
    // I've always seen this sent but never know if it'll be null for privacy at some point
    public string? Username { get; set; }
    public string? GameCode { get; set; }
    public required string GameName { get; set; }
    public string? Rank { get; set; }
    public required DateTimeOffset Time { get; set; }
    public required string CryptoCode { get; set; }
    public required string BetAmountFiat { get; set; }
    public required string BetAmountCrypto { get; set; }
    // Negatives for losses
    public required string WinAmountFiat { get; set; }
    public required string WinAmountCrypto { get; set; }
    // null on losses
    public string? Multiplier { get; set; }
    public string? CategoryIcon { get; set; }
    public List<string>? Types { get; set; }
    public string? Type { get; set; }
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