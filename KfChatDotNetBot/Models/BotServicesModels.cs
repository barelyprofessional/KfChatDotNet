namespace KfChatDotNetBot.Models;

public class KickChannelModel
{
    public required int ChannelId { get; set; }
    public required int ForumId { get; set; }
    public required string ChannelSlug { get; set; }
}

public class CourtHearingModel
{
    public required string Description { get; set; }
    public required DateTimeOffset Time { get; set; }
    public required string CaseNumber { get; set; }
}