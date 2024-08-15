namespace KfChatDotNetBot.Models.DbModels;

public class TwitchViewCountDbModel
{
    public int Id { get; set; }
    public required string Topic { get; set; }
    public required double ServerTime { get; set; }
    public required int Viewers { get; set; }
    public required DateTimeOffset Time { get; set; }
}