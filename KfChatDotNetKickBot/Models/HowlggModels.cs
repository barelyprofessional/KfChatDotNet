using System.Text.Json.Serialization;

namespace KfChatDotNetKickBot.Models;

public class HowlggBetHistoryResponseModel
{
    [JsonPropertyName("user")]
    public required HowlggUserModel User { get; set; }
    [JsonPropertyName("history")]
    public required HowlggHistoryModel History { get; set; }
}

public class HowlggUserModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("netProfit")]
    public long NetProfit { get; set; }
}

public class HowlggHistoryModel
{
    [JsonPropertyName("profit")]
    public required long Profit { get; set; }
    [JsonPropertyName("cumulative")]
    public required long Cumulative { get; set; }
    [JsonPropertyName("data")]
    public required List<HowlggHistoryDataModel> Data { get; set; }
}

public class HowlggHistoryDataModel
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
    [JsonPropertyName("bet")]
    public required long Bet { get; set; }
    [JsonPropertyName("date")]
    // For some reason it has a +2 offset
    public required DateTimeOffset Date { get; set; }
    [JsonPropertyName("game")]
    public required string Game { get; set; }
    [JsonPropertyName("gameId")]
    public required long GameId { get; set; }
    [JsonPropertyName("profit")]
    public required long Profit { get; set; }
}