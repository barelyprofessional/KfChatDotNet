namespace EphyraChatClient.Models.Events;

public class ChatMentionModel
{
    public required string PrincipalUuid { get; set; }
    /// <summary>Whether the mentioned user was in the room when the message was posted</summary>
    public bool Present { get; set; }
}

public class ChatReactionTallyModel
{
    public required string ReactionCode { get; set; }
    public int Count { get; set; }
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Where an entry came from in terms of the room's timeline
/// </summary>
public enum ChatEntryOrigin
{
    /// <summary>Happened while we were sat in the room</summary>
    Live,
    /// <summary>
    /// Part of the backlog (latest 20 or so) the host sends when you join a room without telling it where you were up to.
    /// You almost certainly don't want to act on these
    /// </summary>
    History,
    /// <summary>
    /// Something we missed while disconnected which the host replayed because we rejoined with a cursor
    /// </summary>
    Recovered
}

public class ChatMessageModel
{
    public required string RoomUuid { get; set; }
    /// <summary>Sequence of the log entry that carried this (a post, edit or restore)</summary>
    public required long RoomSeq { get; set; }
    /// <summary>Sequence the message was originally posted at. Edits and reactions never move this</summary>
    public long? PostedSeq { get; set; }
    /// <summary>
    /// The value to hand back as expected_message_state_seq when editing or deleting. The client tracks this itself
    /// so you only need it if you're building frames by hand
    /// </summary>
    public required long MessageStateSeq { get; set; }
    public required string MessageUuid { get; set; }
    public string? EntryUuid { get; set; }
    public required ChatPrincipalModel Author { get; set; }
    /// <summary>
    /// Whoever caused this entry. Same as the author for a post, but could be a moderator for an edit
    /// </summary>
    public string? ActorPrincipalUuid { get; set; }
    /// <summary>
    /// Original message exactly as it was typed, BBCode intact. Not HTML encoded.
    /// Mentions are in the form [user=principal_uuid]Name[/user]
    /// </summary>
    public required string BodyBbcode { get; set; }
    /// <summary>
    /// Message rendered into HTML (the contents of the row's chat-body). Empty if the host didn't send a row
    /// </summary>
    public required string BodyHtml { get; set; }
    /// <summary>The entire row as the web client would insert it into the page</summary>
    public string? RowHtml { get; set; }
    public List<ChatMentionModel> Mentions { get; set; } = [];
    public List<ChatReactionTallyModel> Reactions { get; set; } = [];
    public required DateTimeOffset PostedAt { get; set; }
    /// <summary>When it was last edited, null if it never was</summary>
    public DateTimeOffset? EditedAt { get; set; }
    public int Revision { get; set; } = 1;
    /// <summary>
    /// The nonce you sent the message with. Only set on the live echo of a message posted over this connection, so
    /// don't rely on it if you got disconnected between sending and the echo
    /// </summary>
    public string? ClientNonce { get; set; }
    /// <summary>True if this was imported from the old chat rather than posted here</summary>
    public bool Imported { get; set; }
    public ChatEntryOrigin Origin { get; set; }
}

public class ChatMessageDeletedModel
{
    public required string RoomUuid { get; set; }
    public required long RoomSeq { get; set; }
    public required string MessageUuid { get; set; }
    public string? ActorPrincipalUuid { get; set; }
    public DateTimeOffset? At { get; set; }
    /// <summary>
    /// A tombstoned message can be restored, a purged one is gone for good
    /// </summary>
    public bool Purged { get; set; }
    public string? Reason { get; set; }
    public ChatEntryOrigin Origin { get; set; }
}

public class ChatReactionModel
{
    public required string RoomUuid { get; set; }
    public required long RoomSeq { get; set; }
    public required string MessageUuid { get; set; }
    /// <summary>Who reacted</summary>
    public string? PrincipalUuid { get; set; }
    /// <summary>What they reacted with, null if they took their reaction back</summary>
    public string? ReactionCode { get; set; }
    public DateTimeOffset? At { get; set; }
    /// <summary>Author of the message that got reacted to, if the host sent the row along</summary>
    public string? MessageAuthorPrincipalUuid { get; set; }
    /// <summary>Totals for the message after this reaction</summary>
    public List<ChatReactionTallyModel> Reactions { get; set; } = [];
    public ChatEntryOrigin Origin { get; set; }
}

/// <summary>
/// What could be scraped out of the HTML the host renders for a message
/// </summary>
public class ChatRowModel
{
    public string? MessageUuid { get; set; }
    public string? AuthorPrincipalUuid { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public List<string> Badges { get; set; } = [];
    public long? RoomSeq { get; set; }
    public long? PostedSeq { get; set; }
    public long? MessageStateSeq { get; set; }
    public bool Tombstoned { get; set; }
    public bool Revised { get; set; }
    public bool Restored { get; set; }
    public List<string> MentionedPrincipalUuids { get; set; } = [];
    public DateTimeOffset? PostedAt { get; set; }
    public string? BodyHtml { get; set; }
    public List<ChatReactionTallyModel> Reactions { get; set; } = [];
    public required string RowHtml { get; set; }
}
