using System.Net;
using System.Net.WebSockets;
using KickWsClient.Models;
using Newtonsoft.Json;
using Websocket.Client;
using NLog;


namespace KickWsClient;

public class KickWsClient
{
    public event EventHandlers.OnPusherConnectionEstablishedEventHandler OnPusherConnectionEstablished;
    public event EventHandlers.OnPusherSubscriptionSucceededEventHandler OnPusherSubscriptionSucceeded;
    public event EventHandlers.OnPusherPongEventHandler OnPusherPong;
    public event EventHandlers.OnFollowersUpdatedEventHandler OnFollowersUpdated;
    public event EventHandlers.OnChatMessageEventHandler OnChatMessage;
    public event EventHandlers.OnChannelSubscriptionEventHandler OnChannelSubscription;
    public event EventHandlers.OnSubscriptionEventHandler OnSubscription;
    public event EventHandlers.OnMessageDeletedEventHandler OnMessageDeleted;
    public event EventHandlers.OnUserBannedEventHandler OnUserBanned;
    public event EventHandlers.OnUserUnbannedEventHandler OnUserUnbanned;
    public event EventHandlers.OnUpdatedLiveStreamEventHandler OnUpdatedLiveStream;
    public event EventHandlers.OnStopStreamBroadcastEventHandler OnStopStreamBroadcast;
    public event EventHandlers.OnStreamerIsLiveEventHandler OnStreamerIsLive;
    public event EventHandlers.OnWsDisconnectionEventHandler OnWsDisconnection;
    public event EventHandlers.OnWsReconnectEventHandler OnWsReconnect;
    // You really shouldn't use this unless you're extending the functionality of the library, e.g. adding support for
    // not yet implemented message types.
    public event EventHandlers.OnWsMessageReceivedEventHandler OnWsMessageReceived;
    public event EventHandlers.OnPollUpdateEventHandler OnPollUpdate;

    private WebsocketClient _wsClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Uri _kickPusherUri;
    private int _reconnectTimeout;
    private string? _proxy;

    public KickWsClient(
        string kickPusherUri =
            "wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=7.6.0&flash=false",
        string? proxy = null, int reconnectTimeout = 30)
    {
        _kickPusherUri = new Uri(kickPusherUri);
        _proxy = proxy;
        _reconnectTimeout = reconnectTimeout;
    }

    public async Task StartWsClient()
    {
        _logger.Debug("StartWsClient() called, creating client");
        _wsClient = await CreateWsClient();
    }

    public void Disconnect()
    {
        _logger.Debug("Disconnect() called, closing Websocket");
        _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Closing websocket").Wait();
    }

    private async Task<WebsocketClient> CreateWsClient()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            if (_proxy == null) return clientWs;
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });

        var client = new WebsocketClient(_kickPusherUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout)
        };

        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _logger.Debug("Websocket client has been built, about to start");
        await client.Start();
        _logger.Debug("Websocket client started!");
        return client;
    }
    
    public bool IsConnected()
    {
        return _wsClient is { IsRunning: true };
    }

    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from the chat (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        if (reconnectionInfo.Type == ReconnectionType.Initial)
        {
            _logger.Error("Not firing the reconnection event as this is the initial event");
            return;
        }
        OnWsReconnect?.Invoke(this, reconnectionInfo);
    }

    /// <summary>
    /// Send a generic Pusher packet
    /// </summary>
    /// <param name="eventName">Event name</param>
    /// <param name="data">Event data</param>
    public void SendPusherPacket(string eventName, object data)
    {
        var pkt = new PusherModels.BasePusherRequestModel { Event = eventName, Data = data};
        var json = JsonConvert.SerializeObject(pkt);
        _logger.Debug("Sending message to Pusher");
        _logger.Debug(json);
        _wsClient.Send(json); 
    }

    /// <summary>
    /// Send a ping packet. You should expect a pong response immediately after
    /// </summary>
    public void SendPusherPing()
    {
        SendPusherPacket("pusher:ping", new object());
    }

    /// <summary>
    /// Send a pusher subscribe packet to subscribe to a channel or chatroom. You should receive a subscription succeeded packet
    /// </summary>
    /// <param name="channel">Channel string e.g. channel.2515504</param>
    /// <param name="auth">Optional authentication string. Empty string means guest</param>
    public void SendPusherSubscribe(string channel, string auth = "")
    {
        var subPacket = new PusherModels.PusherSubscribeRequestModel { Auth = auth, Channel = channel };
        SendPusherPacket("pusher:subscribe", subPacket);
    }
    
    /// <summary>
    /// Send pusher unsubscribe packet to unsub from a channel or chatroom. Expect no response
    /// </summary>
    /// <param name="channel">Channel string e.g. channel.2515504</param>
    public void SendPusherUnsubscribe(string channel)
    {
        var unsubPacket = new PusherModels.PusherUnsubscribeRequestModel { Channel = channel };
        SendPusherPacket("pusher:unsubscribe", unsubPacket);
    }

    private void WsMessageReceived(ResponseMessage message)
    {
        OnWsMessageReceived?.Invoke(this, message);

        if (message.Text == null)
        {
            _logger.Info("Websocket message was null, ignoring packet");
            return;
        }

        PusherModels.BasePusherEventModel pusherMsg;
        try
        {
            pusherMsg = JsonConvert.DeserializeObject<PusherModels.BasePusherEventModel>(message.Text) ??
                        throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            _logger.Error("Failed to parse Pusher message. Exception follows:");
            _logger.Error(e);
            _logger.Error("--- Message from Pusher follows ---");
            _logger.Error(message.Text);
            _logger.Error("--- /end of message ---");
            return;
        }
        
        _logger.Debug($"Pusher event receievd: {pusherMsg.Event}");

        switch (pusherMsg.Event)
        {
            case "pusher:connection_established":
            {
                var data =
                    JsonConvert.DeserializeObject<PusherModels.PusherConnectionEstablishedEventModel>(pusherMsg.Data);
                OnPusherConnectionEstablished?.Invoke(this, data);
                return;
            }
            case "pusher_internal:subscription_succeeded":
                OnPusherSubscriptionSucceeded?.Invoke(this, pusherMsg);
                return;
            case "pusher:pong":
                OnPusherPong?.Invoke(this, pusherMsg);
                return;
            case @"App\Events\FollowersUpdated":
            {
                var data = JsonConvert.DeserializeObject<KickModels.FollowersUpdatedEventModel>(pusherMsg.Data);
                OnFollowersUpdated?.Invoke(this, data);
                return;
            }
            case @"App\Events\ChatMessageEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.ChatMessageEventModel>(pusherMsg.Data);
                OnChatMessage?.Invoke(this, data);
                return;
            }
            case @"App\Events\ChannelSubscriptionEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.ChannelSubscriptionEventModel>(pusherMsg.Data);
                OnChannelSubscription?.Invoke(this, data);
                return;
            }
            case @"App\Events\SubscriptionEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.SubscriptionEventModel>(pusherMsg.Data);
                OnSubscription?.Invoke(this, data);
                return;
            }
            case @"App\Events\MessageDeletedEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.MessageDeletedEventModel>(pusherMsg.Data);
                OnMessageDeleted?.Invoke(this, data);
                return;
            }
            case @"App\Events\UserBannedEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.UserBannedEventModel>(pusherMsg.Data);
                OnUserBanned?.Invoke(this, data);
                return;
            }
            case @"App\Events\UserUnbannedEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.UserUnbannedEventModel>(pusherMsg.Data);
                OnUserUnbanned?.Invoke(this, data);
                return;
            }
            case @"App\Events\LiveStream\UpdatedLiveStreamEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.UpdatedLiveStreamEventModel>(pusherMsg.Data);
                OnUpdatedLiveStream?.Invoke(this, data);
                return;
            }
            case @"App\Events\StopStreamBroadcast":
            {
                var data = JsonConvert.DeserializeObject<KickModels.StopStreamBroadcastEventModel>(pusherMsg.Data);
                OnStopStreamBroadcast?.Invoke(this, data);
                return;
            }
            case @"App\Events\StreamerIsLive":
            {
                var data = JsonConvert.DeserializeObject<KickModels.StreamerIsLiveEventModel>(pusherMsg.Data);
                OnStreamerIsLive?.Invoke(this, data);
                return;
            }
            case @"App\Events\PollUpdateEvent":
            {
                var data = JsonConvert.DeserializeObject<KickModels.PollUpdateEventModel>(pusherMsg.Data);
                OnPollUpdate?.Invoke(this, data);
                return;
            }
            default:
                _logger.Info("Event unhandled. JOSN payload follows");
                _logger.Info(message.Text);
                break;
        }
    }
}