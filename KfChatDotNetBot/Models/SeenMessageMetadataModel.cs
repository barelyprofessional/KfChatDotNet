namespace KfChatDotNetBot.Models;

public class SeenMessageMetadataModel
{
    public string MessageUuid { get; set; }
    public DateTimeOffset? LastEdited { get; set; }
}