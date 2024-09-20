namespace KfChatDotNetBot.Models;

public class SeenMessageMetadataModel
{
    public int MessageId { get; set; }
    public DateTimeOffset? LastEdited { get; set; }
}