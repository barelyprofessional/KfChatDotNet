using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

// {"betIdentifier":"SOL-5190-o36-2hbsgogl7-14000227177584","gameName":"Le Pharaoh","casinoGameId":5190,"username":"xPxrtaL","isPrivate":false,"tierImage":"https://cdn.dev.yeet.com/concierge/tier/Copper.png","thumbnailImage":"https://cdn.yeet.com/casinoGames/assets/s3/hacksaw/LePharaoh96.png","betAmount":1.8,"currencyCode":"SOL","createdAt":"2025-05-11T04:17:31.029Z","actionType":"bet"}
public class YeetCasinoBetModel
{
    [JsonPropertyName("betIdentifier")]
    public required string BetIdentifier { get; set; }
    [JsonPropertyName("gameName")]
    public required string GameName { get; set; }
    [JsonPropertyName("casinoGameId")]
    public int? CasinoGameId { get; set; }
    // Set to "hidden" for private accounts
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("isPrivate")]
    public required bool IsPrivate { get; set; }
    [JsonPropertyName("betAmount")]
    public required double BetAmount { get; set; }
    [JsonPropertyName("currencyCode")]
    public required string CurrencyCode { get; set; }
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("actionType")]
    public required string ActionType { get; set; }
}

// {"betIdentifier":"SOL-2204-hv08k-12a92tcxa-109402263616008","casinoGameId":2204,"gameName":"Mega Roulette","username":"CaoSan","isPrivate":false,"tierImage":"https://cdn.dev.yeet.com/concierge/tier/Silver.png","thumbnailImage":"https://cdn.yeet.com/casinoGames/assets/s3/pragmaticexternal/MegaRoulette.png","winAmount":19.5,"betAmount":4.698795180722891,"currencyCode":"SOL","createdAt":"2025-05-11T04:17:33.142Z","multiplier":4.15,"actionType":"win"}
public class YeetCasinoWinModel : YeetCasinoBetModel
{
    [JsonPropertyName("winAmount")]
    public required double WinAmount { get; set; }
    [JsonPropertyName("multiplier")]
    public required double Multiplier { get; set; }
}

public class SeenYeetBet
{
    public required SentMessageTrackerModel Message { get; set; }
    public required YeetCasinoBetModel Bet { get; set; }
}