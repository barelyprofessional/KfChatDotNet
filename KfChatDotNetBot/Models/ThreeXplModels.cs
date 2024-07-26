using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class ThreeXplEventModel
{
    [JsonPropertyName("block")]
    public int? Block { get; set; }
    [JsonPropertyName("transaction")]
    public required string TransactionHash { get; set; }
    [JsonPropertyName("sort_key")]
    public int? SortKey { get; set; }
    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; } 
    [JsonPropertyName("currency")]
    public required string Currency { get; set; }
    [JsonPropertyName("effect")]
    public required string Effect { get; set; }
    [JsonPropertyName("failed")]
    public bool? Failed { get; set; }
    [JsonPropertyName("extra")]
    public string? Extra { get; set; }
}

public class ThreeXplCurrencyModel
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("symbol")]
    public required string Symbol { get; set; }
    [JsonPropertyName("decimals")]
    public required int Decimals { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ThreeXplAddressModel
{
    [JsonPropertyName("address")]
    public required string Address { get; set; }
    [JsonPropertyName("balances")]
    public required Dictionary<string, int?> Balances { get; set; }
    [JsonPropertyName("events")]
    public required Dictionary<string, int?> Events { get; set; }
}

