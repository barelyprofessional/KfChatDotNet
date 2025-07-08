namespace KfChatDotNetBot.Models;

public class ChipsggBetModel
{
    public DateTimeOffset Created { get; set; }
    // Can actually get the duration of a game from this
    public DateTimeOffset Updated { get; set; }
    public string? UserId { get; set; }
    // Sometimes null for no discernible reason
    public string? Username { get; set; }
    // Win of any amount even if it's less than a 1x multi
    public bool Win { get; set; }
    public double Winnings { get; set; }
    public string? GameTitle { get; set; }
    public double Amount { get; set; }
    public float Multiplier { get; set; }
    public string? Currency { get; set; }
    public float CurrencyPrice { get; set; }
    public string? BetId { get; set; }
}

public class ChipsggCurrencyModel
{
    public required string Name { get; set; }
    public required int Decimals { get; set; }
    public float? Price { get; set; }
    public required bool Hidden { get; set; }
}