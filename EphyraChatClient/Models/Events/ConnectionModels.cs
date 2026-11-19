namespace EphyraChatClient.Models.Events;

public class ChatWelcomeModel
{
    /// <summary>Who the chat host thinks we are</summary>
    public string? PrincipalUuid { get; set; }
    public string? DisplayName { get; set; }
    public required string LinkState { get; set; }
    public string? EmoticonRevision { get; set; }
    public bool? UploadsAvailable { get; set; }
    public List<string> SideRoomUuids { get; set; } = [];
}

public class ChatDisconnectedModel
{
    /// <summary>False if the socket never even opened (refused upgrade, network failure)</summary>
    public bool WasConnected { get; set; }
    /// <summary>True if we'd made it as far as the welcome frame</summary>
    public bool WasWelcomed { get; set; }
    /// <summary>
    /// WebSocket close code if there was one. Ephyra specific ones are 4001/4003 (session refused), 4004 (banned),
    /// 4008 (we closed it over a missed pong), 4009 (stale) and 4010 (not permitted)
    /// </summary>
    public int? CloseCode { get; set; }
    public string? CloseDescription { get; set; }
    /// <summary>HTTP status of the upgrade if it was refused. 401 means the session cookie is missing or dead</summary>
    public int? UpgradeStatusCode { get; set; }
    public Exception? Exception { get; set; }
    /// <summary>
    /// False means the client has given up (banned, not permitted, or you stopped it) and nothing more will happen
    /// until you call ReconnectAsync or StartAsync
    /// </summary>
    public bool WillReconnect { get; set; }
    /// <summary>Why the client gave up if <see cref="WillReconnect"/> is false</summary>
    public string? HaltReason { get; set; }
}

public enum ChatAdmissionFailure
{
    /// <summary>The forum itself said no (401/403). Usually means the forum cookies are dead</summary>
    ForumRefused,
    /// <summary>The forum (or whatever is in front of it) didn't give us a ticket</summary>
    ForumUnreachable,
    /// <summary>Got a ticket, but the chat host wouldn't swap it for a session</summary>
    ChatHostRefused,
    /// <summary>The ticket provider you supplied threw</summary>
    TicketProviderFailed
}

public class ChatAdmissionFailedModel
{
    public required ChatAdmissionFailure Failure { get; set; }
    public int? StatusCode { get; set; }
    public Exception? Exception { get; set; }
}

public class ChatRoomJoinedModel
{
    public required string RoomUuid { get; set; }
    /// <summary>listed or side</summary>
    public string? RoomKind { get; set; }
    /// <summary>Sequence of the newest entry in the room at the time of joining</summary>
    public long? HeadSeq { get; set; }
    /// <summary>
    /// True if we told the host where we were up to, meaning what follows is only what was missed. False means
    /// what follows is the regular backlog
    /// </summary>
    public bool Resumed { get; set; }
}

public class ChatRoomJoinFailedModel
{
    public required string RoomUuid { get; set; }
    public string? Code { get; set; }
    /// <summary>True if the host denied us outright, false if the client is going to try again shortly</summary>
    public bool Terminal { get; set; }
}

public class ChatErrorModel
{
    public string? Code { get; set; }
    /// <summary>
    /// What the refused frame was about. For a message you tried to post it's the client nonce, for a reaction it's the
    /// message UUID
    /// </summary>
    public string? Ref { get; set; }
    public bool IsTransient { get; set; }
}

public enum ChatCommandStatus
{
    Applied,
    /// <summary>The host turned it down, see Code</summary>
    Refused,
    /// <summary>The message changed underneath us and a retry with fresh state didn't help either</summary>
    Conflict,
    /// <summary>Never heard back. It may or may not have been applied</summary>
    TimedOut,
    /// <summary>Couldn't be sent as the chat isn't connected (or the link is frozen)</summary>
    NotConnected,
    /// <summary>Don't know the message's current state and the host wouldn't tell us</summary>
    UnknownMessage
}

public class ChatCommandResultModel
{
    public required ChatCommandStatus Status { get; set; }
    public string? Code { get; set; }
    public string? MessageUuid { get; set; }
    public long? RoomSeq { get; set; }
    public long? MessageStateSeq { get; set; }
    public bool Success => Status == ChatCommandStatus.Applied;
}

public class ChatRoomHeadModel
{
    public required string RoomUuid { get; set; }
    public long? HeadSeq { get; set; }
    /// <summary>The whole JSON object for the room as the host sent it, in case there's a name or something in there</summary>
    public string? RawJson { get; set; }
}

public class ChatWhoisModel
{
    public required string PrincipalUuid { get; set; }
    /// <summary>Forum user ID</summary>
    public required int UserId { get; set; }
    public string? Username { get; set; }
}
