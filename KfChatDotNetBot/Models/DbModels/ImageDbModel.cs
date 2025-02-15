namespace KfChatDotNetBot.Models.DbModels;

public class ImageDbModel
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Url { get; set; }
    public required DateTimeOffset LastSeen { get; set; }
}