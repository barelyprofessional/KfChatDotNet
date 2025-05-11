using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Yeet : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://api.yeet.com/room-service/socket/?EIO=4&transport=websocket\n");
    private int _reconnectTimeout = 30;
    private string? _proxy;
    public delegate void OnYeetBetEventHandler(object sender, YeetCasinoBetModel data);
    public delegate void OnYeetWinEventHandler(object sender, YeetCasinoWinModel data);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnYeetBetEventHandler OnYeetBet;
    public event OnYeetWinEventHandler OnYeetWin;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;

    public Yeet(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Yeet WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://yeet.com");
            clientWs.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout),
            IsReconnectionEnabled = false
        };
        
        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _wsClient = client;
        
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
        _logger.Error($"Client disconnected from Yeet (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        if (reconnectionInfo.Type == ReconnectionType.Initial)
        {
            _logger.Info("Sending subscribe payload to Yeet");
            _wsClient.Send("40/public,");

        }
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Yeet sent a null message");
            return;
        }
        _logger.Trace($"Received event from Yeet: {message.Text}");

        try
        {
            var packetType = message.Text.Split('/')[0];
            if (packetType == "2")
            {
                _logger.Info("Received ping from Yeet, replying with pong");
                _wsClient.Send("3");
                return;
            }
            
            if (packetType == "42")
            {
                var data = JsonSerializer.Deserialize<List<JsonElement>>(message.Text.Replace("42/public,",
                    string.Empty));
                if (data[0].GetString() == "casino-bet")
                {
                    OnYeetBet?.Invoke(this, data[1].Deserialize<YeetCasinoBetModel>());
                    return;
                }
                if (data[0].GetString() == "casino-win")
                {
                    OnYeetWin?.Invoke(this, data[1].Deserialize<YeetCasinoWinModel>());
                    return;

                }
                _logger.Info($"Event {data[0].GetString()} from Yeet was not handled");
                _logger.Info(message.Text);
                return;
            }

            if (message.Text == "40")
            {
                _logger.Info("Yeet has replied to the subscription packet");
                return;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Yeet");
            _logger.Error(e);
            _logger.Error("--- Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of Payload ---");
        }
    }

    public void Dispose()
    {
        _wsClient.Dispose();
        GC.SuppressFinalize(this);
    }
}