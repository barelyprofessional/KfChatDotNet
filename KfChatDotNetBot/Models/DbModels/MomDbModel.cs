namespace KfChatDotNetBot.Models.DbModels;

public class MomDbModel
{
    public int Id { get; set; }
    public required UserDbModel User { get; set; }
    public DateTimeOffset Time { get; set; }
}