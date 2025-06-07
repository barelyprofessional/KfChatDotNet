namespace KfChatDotNetBot.Models.DbModels;

public class PocketWatchAddressDbModel
{
    public int Id { get; set; }
    public required string Network { get; set; }
    public required string Address { get; set; }
    public required string Label { get; set; }
    public required bool BypassGambaSeshPresenceDetection { get; set; }
    public required int CheckIntervalSec { get; set; }
    public required DateTimeOffset LastChecked { get; set; } = DateTimeOffset.UtcNow;
}

public class PocketWatchTransactionDbModel
{
    public int Id { get; set; }
    public required string TransactionHash { get; set; }
    public required DateTimeOffset Time { get; set; }
    public required string Currency { get; set; }
    public required string Effect { get; set; }
    public required string Network { get; set; }
    public required string Address { get; set; }
    public required long Balance { get; set; }
    public required float UsdRate { get; set; }
    public required bool IsMempool { get; set; }
}