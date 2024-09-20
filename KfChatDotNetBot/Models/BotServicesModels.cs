namespace KfChatDotNetBot.Models;

public class KickChannelModel
{
    public required int ChannelId { get; set; }
    public required int ForumId { get; set; }
    public required string ChannelSlug { get; set; }
}