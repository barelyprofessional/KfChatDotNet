using KfChatDotNetWsClient.Models.Json;
using Websocket.Client;

namespace KfChatDotNetWsClient.Models.Events;

public class EventHandlers
{
    public delegate void OnMessagesEventHandler(object sender, List<MessageModel> messages,
        MessagesJsonModel jsonPayload);

    // When a user first joins the chat, this event will fire with the entire user list (which may be massive)
    // But when users join in the course of a regular chat, it'll be one at a time
    public delegate void OnUsersJoinedEventHandler(object sender, List<UserModel> users, UsersJsonModel jsonPayload);

    // Usually only one user parts at a time, but theoretically the model could support more than one at a time
    public delegate void OnUsersPartedEventHandler(object sender, List<int> userIds);

    public delegate void OnWsReconnectEventHandler(object sender, ReconnectionInfo reconnectionInfo);

    // Usually only one is sent at a time but it is a list hence the pluralization
    public delegate void OnDeleteMessagesEventHandler(object sender, List<string> messageIds);

    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo disconnectionInfo);

    public delegate void OnFailedToJoinRoom(object sender, string message);

    public delegate void OnUnknownCommand(object sender, string message);

    public delegate void OnPermissionsEventHandler(object sender, PermissionsJsonModel permissions);

    public delegate void OnSystemMessage(object sender, string message);
}