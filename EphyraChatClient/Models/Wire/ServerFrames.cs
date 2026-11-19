using System.Text.Json;
using System.Text.Json.Serialization;

namespace EphyraChatClient.Models.Wire;

// Everything in here is a 1:1 mapping of what the chat host sends over the socket. The "t" property decides which of
// these a frame gets deserialized into. Unknown properties are ignored so the host can grow without breaking us.

// {"t":"welcome","principal_uuid":"019e618a-...","display_name":"Flaming Dumpster","link_state":"linked",
//  "emoticon_revision":"4972343c1ae09a68","uploads_available":false,"side_rooms":[]}
public class WelcomeFrame
{
    [JsonPropertyName("principal_uuid")]
    public string? PrincipalUuid { get; set; }
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
    [JsonPropertyName("link_state")]
    public string? LinkState { get; set; }
    [JsonPropertyName("emoticon_revision")]
    public string? EmoticonRevision { get; set; }
    [JsonPropertyName("uploads_available")]
    public bool? UploadsAvailable { get; set; }
    [JsonPropertyName("side_rooms")]
    public List<SideRoomFrame>? SideRooms { get; set; }
}

public class SideRoomFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public string? ChatRoomUuid { get; set; }
    [JsonPropertyName("head_seq")]
    public long? HeadSeq { get; set; }
}

// {"t":"joined","chat_room_uuid":"01a1f0f8-...","chat_room_kind":"listed","head_seq":1594}
public class JoinedFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("chat_room_kind")]
    public string? ChatRoomKind { get; set; }
    [JsonPropertyName("head_seq")]
    public long? HeadSeq { get; set; }
    // Only sent for side rooms
    [JsonPropertyName("members")]
    public List<PrincipalFrame>? Members { get; set; }
}

// {"t":"left","chat_room_uuid":"...","reason":"..."}
public class LeftFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

// Used for "resync" and "elided", which carry nothing else we care about
public class RoomFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("room_seq")]
    public long? RoomSeq { get; set; }
}

// {"t":"presence","chat_room_uuid":"...","principals":[{"principal_uuid":"...","display_name":"Null",
//  "avatar_url":"https://kiwifarms.net/data/avatars/m/0/1.avif?1793536131","badges":["green","hidden","primary"]}]}
// Note this is always the whole list for the room, never a delta
public class PresenceFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("principals")]
    public List<PrincipalFrame>? Principals { get; set; }
}

public class PrincipalFrame
{
    [JsonPropertyName("principal_uuid")]
    public string? PrincipalUuid { get; set; }
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
    [JsonPropertyName("badges")]
    public List<string>? Badges { get; set; }
}

public class MentionFrame
{
    [JsonPropertyName("principal_uuid")]
    public string? PrincipalUuid { get; set; }
    [JsonPropertyName("present")]
    public bool Present { get; set; }
}

/// <summary>
/// One entry of a room's log. What's populated depends on <see cref="Kind"/>, see <see cref="EntryKinds"/>
/// </summary>
public class EntryFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public required string ChatRoomUuid { get; set; }
    [JsonPropertyName("room_seq")]
    public long RoomSeq { get; set; }
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
    [JsonPropertyName("entry_uuid")]
    public string? EntryUuid { get; set; }
    /// <summary>Unix timestamp (seconds) of when this entry was written to the log</summary>
    [JsonPropertyName("at")]
    public long? At { get; set; }
    [JsonPropertyName("actor_principal_uuid")]
    public string? ActorPrincipalUuid { get; set; }
    [JsonPropertyName("imported")]
    public bool Imported { get; set; }
    [JsonPropertyName("chat_message_uuid")]
    public string? ChatMessageUuid { get; set; }
    [JsonPropertyName("revision")]
    public int? Revision { get; set; }
    [JsonPropertyName("body_bbcode")]
    public string? BodyBbcode { get; set; }
    [JsonPropertyName("mentions")]
    public List<MentionFrame>? Mentions { get; set; }
    [JsonPropertyName("author_principal_uuid")]
    public string? AuthorPrincipalUuid { get; set; }
    // Never seen this as anything but null on a posted entry so not assuming what type it is
    [JsonPropertyName("edited_at")]
    public JsonElement? EditedAt { get; set; }
    [JsonPropertyName("html")]
    public string? Html { get; set; }
    /// <summary>Only present on the live echo of a message this connection posted. Replays don't carry it</summary>
    [JsonPropertyName("client_nonce")]
    public string? ClientNonce { get; set; }
    // chat_message_reaction_set
    [JsonPropertyName("principal_uuid")]
    public string? PrincipalUuid { get; set; }
    [JsonPropertyName("reaction_code")]
    public string? ReactionCode { get; set; }
    // Moderation style entries (tombstone, revise, sanctions)
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("notify_author")]
    public bool? NotifyAuthor { get; set; }
    [JsonPropertyName("message_state_seq")]
    public long? MessageStateSeq { get; set; }
    // chat_room_subtitle_set
    [JsonPropertyName("chat_room_subtitle")]
    public string? ChatRoomSubtitle { get; set; }
    // chat_side_room_member_added / chat_side_room_member_left
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
    /// <summary>Anything else the host sent that isn't modelled above</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public static class EntryKinds
{
    public const string MessagePosted = "chat_message_posted";
    public const string MessageRevised = "chat_message_revised";
    public const string MessageRestored = "chat_message_restored";
    public const string MessageTombstoned = "chat_message_tombstoned";
    public const string MessagePurged = "chat_message_purged";
    public const string MessageReactionSet = "chat_message_reaction_set";
    public const string RoomSubtitleSet = "chat_room_subtitle_set";
    public const string SanctionApplied = "chat_sanction_applied";
    public const string SanctionLifted = "chat_sanction_lifted";
    public const string SideRoomMemberAdded = "chat_side_room_member_added";
    public const string SideRoomMemberLeft = "chat_side_room_member_left";
}

// {"t":"command_ack","chat_operation_uuid":"22f71d67-...","intent_digest":"ae91...","status":"applied",
//  "outcome":"applied","chat_room_uuid":"...","chat_message_uuid":"...","room_seq":1655,"message_state_seq":1655}
public class CommandAckFrame
{
    [JsonPropertyName("chat_operation_uuid")]
    public string? ChatOperationUuid { get; set; }
    [JsonPropertyName("intent_digest")]
    public string? IntentDigest { get; set; }
    /// <summary>pending, applied, refused or conflict</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    [JsonPropertyName("chat_room_uuid")]
    public string? ChatRoomUuid { get; set; }
    [JsonPropertyName("chat_message_uuid")]
    public string? ChatMessageUuid { get; set; }
    [JsonPropertyName("room_seq")]
    public long? RoomSeq { get; set; }
    [JsonPropertyName("message_state_seq")]
    public long? MessageStateSeq { get; set; }
}

// {"t":"error","code":"reaction_unknown","ref":"01a1f19e-..."}
// ref is whatever the refused frame was about: an operation UUID, a client nonce, a room UUID or a message UUID
public class ErrorFrame
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }
}

public static class ErrorCodes
{
    public const string RateLimited = "rate_limited";
    public const string Unavailable = "unavailable";
    public const string InternalError = "internal_error";
    public const string MessageStateConflict = "message_state_conflict";
    public const string OperationNotFound = "operation_not_found";
    public const string NotPermitted = "not_permitted";

    /// <summary>The host couldn't deal with it right now. Says nothing about whether it was committed or not</summary>
    public static bool IsTransient(string? code) => code is RateLimited or Unavailable or InternalError;

    /// <summary>Codes which mean there's no point trying to join the room again</summary>
    public static bool IsRoomDenial(string? code) =>
        code is "room_not_found" or NotPermitted or "side_room_refused" or "banned" or "principal_banned";
}

// {"t":"link_state","state":"frozen","frozen_since":...}
public class LinkStateFrame
{
    [JsonPropertyName("state")]
    public string? State { get; set; }
    [JsonPropertyName("frozen_since")]
    public JsonElement? FrozenSince { get; set; }
}

// {"t":"rendered","chat_room_uuid":"...","chat_message_uuid":"...","html":"<li ...>"}
// Sent when the host re-rendered a message without anything about it changing (e.g. an embed finished loading)
public class RenderedFrame
{
    [JsonPropertyName("chat_room_uuid")]
    public string? ChatRoomUuid { get; set; }
    [JsonPropertyName("chat_message_uuid")]
    public string? ChatMessageUuid { get; set; }
    [JsonPropertyName("html")]
    public string? Html { get; set; }
}
