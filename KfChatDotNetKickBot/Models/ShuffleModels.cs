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

// {
//     "data": {
//         "user": {
//             "id": "a98f83c3-89b7-4e8d-9e11-7dcf1a89b1ba",
//             "username": "TheBossmanJack",
//             "vipLevel": "SAPPHIRE_3",
//             "createdAt": "2024-06-16T23:17:58.533Z",
//             "avatar": 36,
//             "avatarBackground": 1,
//             "bets": 9450,
//             "usdWagered": "3518595.04092444",
//             "__typename": "User"
//         }
//     }
// }
public class ShuffleUserModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("vipLevel")]
    public required string VipLevel { get; set; }
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("bets")]
    public required int Bets { get; set; }
    [JsonPropertyName("usdWagered")]
    public required string UsdWagered { get; set; }
}