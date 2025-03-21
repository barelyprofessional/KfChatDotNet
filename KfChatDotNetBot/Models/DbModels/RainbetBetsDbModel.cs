namespace KfChatDotNetBot.Models.DbModels;

public class RainbetBetsDbModel
{
    public int Id { get; set; }
    // Weird gibberish identifier given to users, may be hidden on bet feeds for users who opt out of the social shit
    // Null if the user has opted out
    public string? PublicId { get; set; }
    // This is always set. Rainbet never omits the user's ID even if they're anonymous
    public required int RainbetUserId { get; set; }
    public required string GameName { get; set; }
    public required float Value { get; set; }
    public required float Payout { get; set; }
    public required float Multiplier { get; set; }
    public required string BetId { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public required DateTimeOffset BetSeenAt { get; set; }
}