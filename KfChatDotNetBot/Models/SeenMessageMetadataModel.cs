namespace KfChatDotNetBot.Models;

public class SeenMessageMetadataModel
{
    public required string MessageUuid { get; set; }
    public DateTimeOffset? LastEdited { get; set; }
}