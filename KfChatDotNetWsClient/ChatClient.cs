using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Xml;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using NLog;
using Websocket.Client;
// It's a fucking lie. You must use conditional access or you WILL get NullReferenceErrors if an event is not in use
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract

namespace KfChatDotNetWsClient;

public class ChatClient
{
    public event EventHandlers.OnMessagesEventHandler OnMessages;
    public event EventHandlers.OnUsersPartedEventHandler OnUsersParted;
    public event EventHandlers.OnUsersJoinedEventHandler OnUsersJoined;
    public event EventHandlers.OnWsReconnectEventHandler OnWsReconnect;
    public event EventHandlers.OnDeleteMessagesEventHandler OnDeleteMessages;
    public event EventHandlers.OnWsDisconnectionEventHandler OnWsDisconnection;
    public event EventHandlers.OnFailedToJoinRoom OnFailedToJoinRoom;
    public event EventHandlers.OnUnknownCommand OnUnknownCommand;
    private WebsocketClient _wsClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private ChatClientConfigModel _config;
    public DateTime LastPacketReceived = DateTime.UtcNow;

    public ChatClient(ChatClientConfigModel config)
    {
        _config = config;
    }

    public void UpdateConfig(ChatClientConfigModel config)
    {
        _config = config;
    }

    public void UpdateToken(string newToken)
    {
        _config.XfSessionToken = newToken;
    }

    public async Task StartWsClient()
    {
        await CreateWsClient();
    }

    public void Disconnect()
    {
        _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Closing websocket").Wait();
    }

    // Bot is inconsistent with what methods are async and not but I don't want to change the Disconnect() method to be
    // async as then it'll fuck up anyone not waiting for it. This is why there's an explicit async method for this but
    // none for reconnect.
    public async Task DisconnectAsync()
    {
        await _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Closing websocket");
    }

    public async Task Reconnect()
    {
        await _wsClient.Reconnect();
    }

    private async Task CreateWsClient()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            // Guest mode
            if (_config.XfSessionToken == null)
            {
                return clientWs;
            }

            var cookieContainer = new CookieContainer();
            cookieContainer.Add(new Cookie("xf_session", _config.XfSessionToken, "/", _config.CookieDomain));
            clientWs.Options.Cookies = cookieContainer;
            if (_config.Proxy != null)
            {
                clientWs.Options.Proxy = new WebProxy(_config.Proxy);
            }

            return clientWs;
        });

        var client = new WebsocketClient(_config.WsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_config.ReconnectTimeout)
        };
        _wsClient = client;

        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _logger.Debug("Websocket client has been built, about to start");
        await client.Start();
        _logger.Debug("Websocket client started!");
    }

    public bool IsConnected()
    {
        return _wsClient is { IsRunning: true };
    }

    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from the chat (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }

    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        OnWsReconnect?.Invoke(this, reconnectionInfo);
    }

    private void WsMessageReceived(ResponseMessage message)
    {
        LastPacketReceived = DateTime.UtcNow;
        if (message.Text == null)
        {
            _logger.Info("Websocket message was null, ignoring packet");
            return;
        }

        if (message.Text.StartsWith("You cannot join this room"))
        {
            _logger.Debug("Got a message saying we failed to join the room");
            OnFailedToJoinRoom?.Invoke(this, message.Text);
            return;
        }

        if (message.Text.StartsWith("Unknown command"))
        {
            _logger.Debug("Unknown command received");
            OnUnknownCommand?.Invoke(this, message.Text);
            return;
        }

        Dictionary<string, object> packetType = new Dictionary<string, object>();
        try
        {
            packetType = JsonSerializer.Deserialize<Dictionary<string, object>>(message.Text)!;
        }
        catch (Exception e)
        {
            _logger.Error("Failed to parse packet");
            _logger.Error(e);
            _logger.Error($"Packet contents: {message.Text}");
            return;
        }

        _logger.Debug($"Received packet from KF: {string.Join(',', packetType.Keys)}");
        // Message(s) received
        if (packetType.ContainsKey("messages"))
        {
            _logger.Debug("Looks like it's a chat message");
            WsChatMessagesReceived(message);
            return;
        }
        // User(s) joined
        if (packetType.ContainsKey("users"))
        {
            _logger.Debug("Looks like this is a user(s) joined packet");
            WsChatUsersJoined(message);
            return;
        }
        // User(s) parted
        if (packetType.ContainsKey("user"))
        {
            _logger.Debug("Looks like this is a user(s) parted packet");
            WsChatUsersParted(message);
            return;
        }

        if (packetType.ContainsKey("delete"))
        {
            _logger.Debug($"Looks like this is a message deletion packet");
            WsDeleteMessagesReceived(message);
            return;
        }
        
        _logger.Info($"Received packet this was not handled: {message.Text}");
    }

    public void JoinRoom(int roomId)
    {
        _logger.Debug($"Joining {roomId}");
        _wsClient.Send($"/join {roomId}");
    }
    
    public void SendMessage(string message)
    {
        _logger.Debug($"Sending '{message}'");
        _wsClient.Send(message);
    }

    public async Task SendMessageInstantAsync(string message)
    {
        _logger.Debug($"Sending '{message}', bypassing the queue");
        await _wsClient.SendInstant(message);
    }

    public void DeleteMessage(int messageId)
    {
        _logger.Debug($"Deleting {messageId}");
        _wsClient.Send($"/delete {messageId}");
    }

    public void EditMessage(int messageId, string newMessage)
    {
        var payload = JsonSerializer.Serialize(new EditMessageJsonModel {Id = messageId, Message = newMessage});
        _logger.Debug($"Editing {messageId} with '{newMessage}'");
        _wsClient.Send($"/edit {payload}");
    }

    private void WsDeleteMessagesReceived(ResponseMessage message)
    {
        var data = JsonSerializer.Deserialize<DeleteMessagesJsonModel>(message.Text);
        _logger.Debug($"Received delete packet for messages: {string.Join(',', data.MessageIdsToDelete)}");
        OnDeleteMessages?.Invoke(this, data.MessageIdsToDelete);
    }

    private void WsChatMessagesReceived(ResponseMessage message)
    {
        var data = JsonSerializer.Deserialize<MessagesJsonModel>(message.Text);
        var messages = new List<MessageModel>();
        foreach (var chatMessage in data.Messages)
        {
            var model = new MessageModel
            {
                Author = new UserModel
                {
                    Id = chatMessage.Author.Id,
                    Username = chatMessage.Author.Username,
                    AvatarUrl = chatMessage.Author.AvatarUrl,
                    // It isn't sent on chat messages
                    LastActivity = null
                },
                Message = chatMessage.Message,
                MessageId = chatMessage.MessageId,
                MessageRaw = chatMessage.MessageRaw,
                RoomId = chatMessage.RoomId
            };
            
            if(chatMessage.MessageEditDate == 0)
            {
                model.MessageEditDate = null;
            }
            else
            {
                model.MessageEditDate = DateTimeOffset.FromUnixTimeSeconds(chatMessage.MessageEditDate);
            }

            model.MessageDate = DateTimeOffset.FromUnixTimeSeconds(chatMessage.MessageDate);

            messages.Add(model);
        }
        _logger.Debug($"Received {messages.Count} chat messages");
        if (messages.Count == 1)
        {
            _logger.Debug($"{JsonSerializer.Serialize(messages[0])}");
        }
        OnMessages?.Invoke(this, messages, data);
    }

    private void WsChatUsersJoined(ResponseMessage message)
    {
        var data = JsonSerializer.Deserialize<UsersJsonModel>(message.Text);
        var users = new List<UserModel>();
        foreach (var user in data.Users.Keys)
        {
            users.Add(new UserModel
            {
                Id = int.Parse(user),
                Username = data.Users[user].Username,
                AvatarUrl = data.Users[user].AvatarUrl,
                LastActivity = DateTimeOffset.FromUnixTimeSeconds(data.Users[user].LastActivity)
            });
        }
        var usersJoined= data.Users.Select(user => int.Parse(user.Key)).ToList();
        _logger.Debug($"Following users have joined: {string.Join(',', usersJoined)}");
        OnUsersJoined?.Invoke(this, users, data);
    }

    private void WsChatUsersParted(ResponseMessage message)
    {
        // {"user":{"1337":false}}
        var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, bool>>>(message.Text);
        var usersParted = data!["user"].Select(user => int.Parse(user.Key)).ToList();
        _logger.Debug($"Following users have parted: {string.Join(',', usersParted)}");
        OnUsersParted?.Invoke(this, usersParted);
    }
}