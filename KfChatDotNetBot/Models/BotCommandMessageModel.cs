using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Models;

public class BotCommandMessageModel
{
    /// <summary>
    /// Author of the message
    /// </summary>
    public required UserModel Author { get; set; }
    /// <summary>
    /// Recipient of the message if this is a whisper
    /// </summary>
    public UserModel? Recipient { get; set; }
    /// <summary>
    /// Message rendered into HTML
    /// </summary>
    public required string Message { get; set; }
    /// <summary>
    /// Original message with BBCode intact (but HTML-encoded)
    /// </summary>
    public required string MessageRaw { get; set; }
    /// <summary>
    /// Date and time the message was sent
    /// </summary>
    public required DateTimeOffset MessageDate { get; set; }
    /// <summary>
    /// Original message with BBCode intact and HTML decoded
    /// </summary>
    public required string MessageRawHtmlDecoded { get; set; }
    /// <summary>
    /// Chat UUID reference to the message (null for whispers)
    /// </summary>
    public string? MessageUuid { get; set; }
    /// <summary>
    /// When the message was edited (null if never edited or a whisper)
    /// </summary>
    public DateTimeOffset? MessageEditDate { get; set; }
    /// <summary>
    /// Room ID where this message was received. (null if a whisper)
    /// </summary>
    public int? RoomId { get; set; }
    public required bool IsWhisper { get; set; }
}