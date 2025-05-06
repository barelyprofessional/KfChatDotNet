using System.Text.Json.Serialization;

namespace ThreeXplWsClient.Events;

public class BaseThreeXplPacketModel
{
    [JsonPropertyName("connect")]
    public ThreeXplConnectDataModel? Connect { get; set; }
    [JsonPropertyName("id")]
    public int? Id { get; set; }
    [JsonPropertyName("error")]
    public ThreeXplErrorModel? Error { get; set; }
    [JsonPropertyName("subscribe")]
    public ThreeXplSubscribeModel? Subscribe { get; set; }
    [JsonPropertyName("push")]
    public ThreeXplPushModel? Push { get; set; }
    
}

public class ThreeXplDataModel
{
    [JsonPropertyName("blockchain")]
    public string? Blockchain { get; set; }
    [JsonPropertyName("module")]
    public string? Module { get; set; }
    [JsonPropertyName("block")]
    public int? Block { get; set; }
    [JsonPropertyName("transaction")]
    public string? Transaction { get; set; }
    [JsonPropertyName("sort_key")]
    public int? SortKey { get; set; }
    [JsonPropertyName("time")]
    [JsonConverter(typeof(ThreeXplDateTimeConverter))]
    public DateTime Time { get; set; }
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
    [JsonPropertyName("effect")]
    public string? Effect { get; set; }
    [JsonPropertyName("failed")]
    public bool? Failed { get; set; }
    [JsonPropertyName("extra")]
    public object? Extra { get; set; }
    [JsonPropertyName("extra_indexed")]
    public object? ExtraIndexed { get; set; }
    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

public class ThreeXplContextModel
{
    // "time":"0.21778600 1718465848"
    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

public class ThreeXplConnectDataModel
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("ping")]
    public int? Ping { get; set; }
    [JsonPropertyName("pong")]
    public bool? Pong { get; set; }
}

public class ThreeXplErrorModel
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
    [JsonPropertyName("temporary")]
    public bool? Temporary { get; set; }
}

public class ThreeXplSubscribeModel
{
    [JsonPropertyName("recoverable")]
    public bool? Recoverable { get; set; }
    [JsonPropertyName("epoch")]
    public string? Epoch { get; set; }
    [JsonPropertyName("positioned")]
    public bool? Positioned { get; set; }
}

public class ThreeXplPushModel
{
    [JsonPropertyName("channel")]
    public required string Channel { get; set; }
    [JsonPropertyName("pub")]
    public required ThreeXplPushPubModel Pub { get; set; }
    [JsonPropertyName("offset")]
    public int? Offset { get; set; }
    
}

public class ThreeXplPushPubModel
{
    [JsonPropertyName("data")]
    public required ThreeXplPushDataModel Data { get; set; }
}

public class ThreeXplPushDataModel
{
    [JsonPropertyName("data")]
    public required List<ThreeXplDataModel> Data { get; set; }
    [JsonPropertyName("context")]
    public required ThreeXplContextModel Context { get; set; }
}