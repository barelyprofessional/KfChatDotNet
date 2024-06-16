using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using NLog;
using ThreeXplWsClient.Events;
using ThreeXplWsClient.Models;
using Websocket.Client;

namespace ThreeXplWsClient;

public class ThreeXplWsClient
{
    public event EventHandlers.OnWsMessageReceivedEventHandler OnWsMessageReceived;
    public event EventHandlers.OnWsDisconnectionEventHandler OnWsDisconnection;
    public event EventHandlers.OnWsReconnectEventHandler OnWsReconnect;
    public event EventHandlers.OnThreeXplPing OnThreeXplPing;
    public event EventHandlers.OnThreeXplPush OnThreeXplPush;
    public event EventHandlers.OnThreeXplConnect OnThreeXplConnect;
    public event EventHandlers.OnThreeXplError OnThreeXplError;
    public event EventHandlers.OnThreeXplSubscribe OnThreeXplSubscribe;

    private WebsocketClient _wsClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private string? _wsJwt;
    private DateTime _wsJwtLastRetrieved = DateTime.Now;
    private int _wsJwtValidityPeriodSeconds;
    private string? _proxy;
    private int _reconnectTimeout;
    private Uri _wsUri;
    // Basically they have a limit of 10 subscriptions per connection and I have more than 10 addresses to monitor, so
    // I give each connection an ID number as that way I know what addresses need to be resubscribed in the event of a
    // connection drop. This ID is included with every event fired and set when the class is constructed.
    private int _connectionId;

    /// <summary>
    /// Client for the 3xpl WebSocket API
    /// </summary>
    /// <param name="threeXplWsUri">URI for the websocket API, published at https://3xpl.com/data/websocket-api</param>
    /// <param name="proxy">Web proxy to use for the WebSocket connection</param>
    /// <param name="reconnectTimeout">Reconnect timeout, defaults to 30 seconds as 3xpl tells us to expect a ping every 25 seconds</param>
    /// <param name="jwtValidityPeriodSeconds">How long the JWT is valid for. Set to int.MaxValue if you've manually provided a non-expiring token</param>
    /// <param name="jwtApiToken">Manually provide a JWT if you have access to create your own</param>
    /// <param name="connectionId">ID that can be used to differentiate multiple 3xpl connections</param>
    public ThreeXplWsClient(string threeXplWsUri = "wss://stream.3xpl.net", string? proxy = null,
        int reconnectTimeout = 30, int jwtValidityPeriodSeconds = 600, string? jwtApiToken = null, int connectionId = 0)
    {
        _wsUri = new Uri(threeXplWsUri);
        _proxy = proxy;
        _reconnectTimeout = reconnectTimeout;
        _wsJwtValidityPeriodSeconds = jwtValidityPeriodSeconds;
        _wsJwt = jwtApiToken;
        _connectionId = connectionId;
    }

    private async Task RefreshApiToken()
    {
        _logger.Debug("Refreshing the API token");
        if (_wsJwtValidityPeriodSeconds == int.MaxValue)
        {
            _logger.Debug($"Token is non expiring as it is set to {int.MaxValue}");
            return;
        }
        if (_wsJwt != null && _wsJwtLastRetrieved.AddSeconds(_wsJwtValidityPeriodSeconds) >= DateTime.Now)
        {
            _logger.Debug(
                $"Token has not yet expired. Its expiration date is {_wsJwtLastRetrieved.AddSeconds(_wsJwtValidityPeriodSeconds):yyyy-MM-dd HH:mm:ss}");
            return;
        }

        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.Proxy = new WebProxy(_proxy);
            handler.UseProxy = true;
        }

        using var client = new HttpClient(handler);
        var token = await client.GetFromJsonAsync<GetWebsocketTokenModel>("https://3xpl.com/get-websockets-token");
        if (token == null)
        {
            _logger.Error("Caught a null when retrieving a WebSocket JWT from 3xpl");
            throw new InvalidOperationException("Caught a null when retrieving a WebSocket JWT from 3xp");
        }

        _wsJwt = token.Data;
        _wsJwtLastRetrieved = DateTime.Now;
    }
    
    public async Task StartWsClient()
    {
        _logger.Debug("StartWsClient() called, creating client");
        await CreateWsClient();
    }

    private async Task CreateWsClient()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            if (_proxy == null) return clientWs;
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout)
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
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo, _connectionId);
    }

    private void SendConnectRequest()
    {
        if (_wsJwt == null)
        {
            _logger.Error("JWT was null.");
            throw new InvalidOperationException("JWT was null");
        }

        var data = new ConnectRequestModel { Connect = new ConnectRequestTokenModel { Token = _wsJwt } };
        var payload = JsonSerializer.Serialize(data);
        _logger.Debug("Sending the following payload to 3xpl");
        _logger.Debug(payload);
        _wsClient.Send(payload);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        _logger.Info("Refreshing JWT");
        RefreshApiToken().Wait();
        _logger.Info("Sending connect request");
        SendConnectRequest();
        OnWsReconnect?.Invoke(this, reconnectionInfo, _connectionId);
    }

    public void SendSubscribeRequest(string channel)
    {
        var data = new SubscribeRequestModel { Subscribe = new SubscribeRequestChannelModel { Channel = channel }};
        var payload = JsonSerializer.Serialize(data);
        _logger.Debug("Sending the following subscription payload to 3xpl");
        _logger.Debug(payload);
        _wsClient.Send(payload);
    }

    private void WsMessageReceived(ResponseMessage message)
    {
        OnWsMessageReceived?.Invoke(this, message, _connectionId);
        _logger.Debug("Received JSON from 3xpl");
        _logger.Debug(message.Text);

        if (message.Text == null)
        {
            _logger.Info("Websocket message was null, ignoring packet");
            return;
        }

        if (message.Text == "{}")
        {
            _logger.Debug("Received ping from 3xpl. Sending back a pong and invoking event");
            _wsClient.Send("{}");
            OnThreeXplPing?.Invoke(this, _connectionId);
            return;
        }
        
        BaseThreeXplPacketModel threeXplPacket;
        try
        {
            threeXplPacket = JsonSerializer.Deserialize<BaseThreeXplPacketModel>(message.Text) ??
                             throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            _logger.Error("Failed to parse 3xpl payload. Exception follows:");
            _logger.Error(e);
            _logger.Error("--- Message from 3xpl follows ---");
            _logger.Error(message.Text);
            _logger.Error("--- /end of message ---");
            return;
        }

        if (threeXplPacket.Connect != null)
        {
            _logger.Debug("Received connect packet from 3xpl, invoking event");
            OnThreeXplConnect?.Invoke(this, threeXplPacket.Connect, _connectionId);
            return;
        }

        if (threeXplPacket.Push != null)
        {
            _logger.Debug("Received data event from 3xpl");
            OnThreeXplPush?.Invoke(this, threeXplPacket.Push, _connectionId);
            return;
        }

        if (threeXplPacket.Error != null)
        {
            _logger.Debug("Received error packet from 3xpl");
            OnThreeXplError?.Invoke(this, threeXplPacket.Error, _connectionId);
            return;
        }

        if (threeXplPacket.Subscribe != null)
        {
            _logger.Debug("Received subscribe packet from 3xpl");
            OnThreeXplSubscribe?.Invoke(this, threeXplPacket.Subscribe, _connectionId);
            return;
        }
        
        _logger.Error("Failed to handle 3xpl packet");
        _logger.Error("--- Message from 3xpl follows ---");
        _logger.Error(message.Text);
        _logger.Error("--- /end of message ---");
    }
}