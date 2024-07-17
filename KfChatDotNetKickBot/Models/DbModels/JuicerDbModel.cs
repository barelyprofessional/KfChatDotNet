namespace KfChatDotNetKickBot.Models.DbModels;

public class JuicerDbModel
{
    public int Id { get; set; }
    public required UserDbModel User { get; set; }
    public float Amount { get; set; }
    public DateTimeOffset JuicedAt { get; set; }
}