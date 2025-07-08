namespace KfChatDotNetWsClient.Models.Events;

public class MessageModel
{
    public required UserModel Author { get; set; }
    /// <summary>
    /// HTML formatted message
    /// </summary>
    public required string Message { get; set; }
    public required int MessageId { get; set; }
    public DateTimeOffset? MessageEditDate { get; set; }
    public required DateTimeOffset MessageDate { get; set; }
    /// <summary>
    /// Unformatted message with original BB code but retaining HTML encoding
    /// </summary>
    public required string MessageRaw { get; set; }
    public required int RoomId { get; set; }
    /// <summary>
    /// Unformatted message with original BB code and HTML entities decoded
    /// </summary>
    public required string MessageRawHtmlDecoded { get; set; }
}