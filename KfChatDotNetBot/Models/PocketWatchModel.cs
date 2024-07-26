namespace KfChatDotNetBot.Models;

public class PocketWatchModel
{
    public required string Network { get; set; }
    public required string Address { get; set; }
    public required string Label { get; set; }
    public required bool BypassGambaSeshPresenceDetection { get; set; }
    public required int CheckIntervalSec { get; set; }
    // Used internally to detect new transactions
    public required DateTime LastChecked { get; set; } = DateTime.Now;
}

public class PocketWatchEventModel
{
    public required string TransactionHash { get; set; }
    public required DateTimeOffset Time { get; set; }
    public required string Currency { get; set; }
    public required string Effect { get; set; }
    public required string Network { get; set; }
    public required string Address { get; set; }
    public required long Balance { get; set; }
    public required float UsdRate { get; set; }
    public required bool IsMempool { get; set; }
    public required PocketWatchModel PocketWatch { get; set; }
}