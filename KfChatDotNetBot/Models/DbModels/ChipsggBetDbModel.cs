namespace KfChatDotNetBot.Models.DbModels;

public class ChipsggBetDbModel
{
    public int Id { get; set; }
    public required DateTimeOffset Created { get; set; }
    public required DateTimeOffset Updated { get; set; }
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public required bool Win { get; set; }
    public required double Winnings { get; set; }
    public required string GameTitle { get; set; }
    public required double Amount { get; set; }
    public required float Multiplier { get; set; }
    public required string Currency { get; set; }
    public required float CurrencyPrice { get; set; }
    public required string BetId { get; set; }
}