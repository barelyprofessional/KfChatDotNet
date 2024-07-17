namespace KfChatDotNetKickBot.Models.DbModels;

public class HowlggBetsDbModel
{
    public int Id { get; set; }
    public required int UserId { get; set; }
    // Per-user bet ID, counts up based on total # of bets
    public required int BetId { get; set; }
    // Global bet ID
    public required long GameId { get; set; }
    // Cents
    public required long Bet { get; set; }
    // Cents
    public required long Profit { get; set; }
    public required DateTimeOffset Date { get; set; }
    public required string Game { get; set; }
}