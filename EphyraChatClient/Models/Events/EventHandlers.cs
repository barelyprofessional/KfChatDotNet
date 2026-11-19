using EphyraChatClient.Models.Wire;

namespace EphyraChatClient.Models.Events;

// All events are raised one at a time, in order, on a thread of their own. Blocking inside a handler holds up the events
// behind it but never the socket, so it's safe to wait on things like EditMessageAsync from in here.
public class EventHandlers
{
    /// <summary>Socket is up and the host has told us who we are. Rooms get (re)joined automatically after this</summary>
    public delegate void OnConnectedEventHandler(object sender, ChatWelcomeModel welcome);

    public delegate void OnDisconnectedEventHandler(object sender, ChatDisconnectedModel disconnection);

    /// <summary>A new session cookie was issued. Good time to persist GetCookies()</summary>
    public delegate void OnSessionEstablishedEventHandler(object sender);

    public delegate void OnAdmissionFailedEventHandler(object sender, ChatAdmissionFailedModel failure);

    public delegate void OnLinkStateChangedEventHandler(object sender, string linkState);

    public delegate void OnRoomJoinedEventHandler(object sender, ChatRoomJoinedModel room);

    public delegate void OnRoomJoinFailedEventHandler(object sender, ChatRoomJoinFailedModel failure);

    public delegate void OnRoomLeftEventHandler(object sender, string roomUuid, string? reason);

    /// <summary>The complete list of who's in the room. The host sends the lot every time it changes</summary>
    public delegate void OnPresenceEventHandler(object sender, string roomUuid, List<ChatPrincipalModel> principals);

    // The host doesn't have join/part events, these come from comparing each presence list with the one before it.
    // On first join (and after every reconnect) you'll get the entire user list here, which may be massive
    public delegate void OnUsersJoinedEventHandler(object sender, string roomUuid, List<ChatPrincipalModel> principals);

    public delegate void OnUsersPartedEventHandler(object sender, string roomUuid, List<ChatPrincipalModel> principals);

    public delegate void OnMessageEventHandler(object sender, ChatMessageModel message);

    public delegate void OnMessageDeletedEventHandler(object sender, ChatMessageDeletedModel deletion);

    public delegate void OnReactionEventHandler(object sender, ChatReactionModel reaction);

    public delegate void OnSubtitleChangedEventHandler(object sender, string roomUuid, string? subtitle);

    /// <summary>Every log entry regardless of kind, including the ones which also have an event of their own</summary>
    public delegate void OnEntryEventHandler(object sender, EntryFrame entry, ChatEntryOrigin origin);

    /// <summary>
    /// The host refused something that wasn't an edit/delete (those report through their return value) or a join
    /// </summary>
    public delegate void OnErrorEventHandler(object sender, ChatErrorModel error);

    /// <summary>A frame type this client doesn't know about. Raw JSON so you can do something with it</summary>
    public delegate void OnUnknownFrameEventHandler(object sender, string frameType, string json);
}
