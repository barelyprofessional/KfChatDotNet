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
    [Obsolete(ChatBot.WhispersGoneMessage)]
    public UserModel? Recipient { get; set; }
    /// <summary>
    /// Message rendered into HTML
    /// </summary>
    public required string Message { get; set; }
    /// <summary>
    /// Original message with BBCode intact. No longer HTML-encoded as Ephyra sends the real thing.
    /// Mentions are flattened from [user=uuid]Name[/user] to @Name, same as <see cref="MessageRawHtmlDecoded"/>
    /// </summary>
    public required string MessageRaw { get; set; }
    /// <summary>
    /// Date and time the message was sent
    /// </summary>
    public required DateTimeOffset MessageDate { get; set; }
    /// <summary>
    /// Original message with BBCode intact and mentions flattened from [user=uuid]Name[/user] to @Name.
    /// This is what command patterns get matched against
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
    /// UUID of the room where this message was received
    /// </summary>
    public string? RoomUuid { get; set; }
    /// <summary>
    /// Message exactly as the chat sent it, [user=uuid]Name[/user] mentions and all
    /// </summary>
    public string? MessageBbCode { get; set; }
    /// <summary>
    /// Principal UUIDs of everyone mentioned in the message
    /// </summary>
    public List<string> MentionedPrincipalUuids { get; set; } = [];
    /// <summary>
    /// Always false now, Ephyra has no whispers
    /// </summary>
    [Obsolete(ChatBot.WhispersGoneMessage)]
    public bool IsWhisper { get; set; }
}