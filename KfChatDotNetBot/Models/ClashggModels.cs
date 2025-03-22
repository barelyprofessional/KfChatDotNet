using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public enum ClashggGame
{
    Plinko,
    Mines,
    Keno
}

public enum ClashggCurrency
{
    Real, // Gems
    Fake // Coins
}

public class ClashggBetModel
{
    public required ClashggGame Game { get; set; }
    public required int UserId { get; set; }
    // Isn't sent for Plinko
    public string? Username { get; set; }
    // Clash.gg uses its own currency for money in play
    // Bets are in cents
    public required int Bet { get; set; }
    // Clash.gg has a fake worthless currency called Coins.
    // Sweepstakes bullshit loophole, the real money is Gems
    // It's even identified as REAL when it's Gems, PLAY when it's Coins
    public required ClashggCurrency Currency { get; set; }
    // Mines doesn't send a multi, but it's calculated based on payout / bet
    public required float Multiplier { get; set; }
    // Payouts aren't sent for Plinko but will be calculated based on multi
    public required float Payout { get; set; }
}

// There's a bunch more properties, but I don't care
// {"id":3122766,"role":"user","name":"nettspend","avatar":"https://avatars.steamstatic.com/6ceb09420f55ca4e84769169fad1436c0f1b6053_full.jpg","xp":28452,"isVerified":false,"isPrivate":false,"premiumUntil":null}
public class ClashggWsUserModel
{
    [JsonPropertyName("user")]
    public required int Id { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

// {"point":257.4683393753577,"avatarUrl":"https://avatars.steamstatic.com/ee5758f75e9ccff825ece5b1a4cb505af851025d_full.jpg","userId":635455,"rows":16,"betAmount":50,"multiplier":0.2,"currency":"REAL"}
public class ClashggWsPlinkoModel
{
    [JsonPropertyName("userId")]
    public required int UserId { get; set; }
    [JsonPropertyName("betAmount")]
    public required int BetAmount { get; set; }
    [JsonPropertyName("multiplier")]
    public required float Multiplier { get; set; }
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
}

// {"updatedAt":"2025-03-22T04:48:55.644Z","status":"playing","currency":"REAL","betAmount":75,"payout":91,"mineCount":1,"user":{"id":3122766,"role":"user","name":"nettspend","avatar":"https://avatars.steamstatic.com/6ceb09420f55ca4e84769169fad1436c0f1b6053_full.jpg","xp":28452,"isVerified":false,"isPrivate":false,"premiumUntil":null}}
public class ClashggWsMinesModel
{
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("betAmount")]
    public required int BetAmount { get; set; }
    [JsonPropertyName("payout")]
    public required int Payout { get; set; }
    [JsonPropertyName("user")]
    public required ClashggWsUserModel User { get; set; }
}

// {"id":4168183,"createdAt":"2025-03-22T04:53:22.008Z","userPicks":[24,36,2,16,29,32,15,10,38,3],"kenoPicks":[11,40,21,33,17,30,6,7,20,8],"payout":0,"multiplier":0,"currency":"REAL","betAmount":50,"user":{"id":2229575,"role":"user","name":"SomeoneSomebody","avatar":"https://avatars.steamstatic.com/f1828607eac4054560a02da9ba83e4310053661a_full.jpg","xp":197137,"isVerified":false,"isPrivate":false,"premiumUntil":null}}
public class ClashggWsKenoModel
{
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("betAmount")]
    public required int BetAmount { get; set; }
    [JsonPropertyName("payout")]
    public required int Payout { get; set; }
    [JsonPropertyName("user")]
    public required ClashggWsUserModel User { get; set; }
    [JsonPropertyName("multiplier")]
    public required float Multiplier { get; set; }
}