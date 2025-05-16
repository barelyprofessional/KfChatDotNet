using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class RainbetWs : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://socket.rainbet.com/socket.io/?EIO=4&transport=websocket");
    private int _reconnectTimeout = 30;
    private string? _proxy;
    public delegate void OnRainbetBetEventHandler(object sender, RainbetWsBetModel bet);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnRainbetBetEventHandler OnRainbetBet;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;

    public RainbetWs(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Rainbet WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://rainbet.com");
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
        _logger.Error($"Client disconnected from Rainbet (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Rainbet sent a null message");
            return;
        }
        _logger.Trace($"Received event from Rainbet: {message.Text}");

        try
        {
            if (message.Text.StartsWith("0{"))
            {
                // 0{"sid":"Pf940zhaAqb6BHBiSgLa","upgrades":[],"pingInterval":25000,"pingTimeout":20000,"maxPayload":100000000}
                _logger.Info("Received initial connection message from Rainbet, sending subscribe");
                _wsClient.Send("40/game-history,{}");
                return;
            }
            var packetType = message.Text.Split('/')[0];
            if (packetType == "2")
            {
                _logger.Info("Received ping from Rainbet, replying with pong");
                _wsClient.Send("3");
                return;
            }
            
            // On subscription
            //40/game-history,{"sid":"7X4UUCSv8BFXgBT1SgLd"}
            if (packetType == "40")
            {
                _logger.Info("Subscribed to Rainbet game history");
                return;
            }
            
            if (packetType == "42")
            {
                var data = JsonSerializer.Deserialize<List<JsonElement>>(message.Text.Replace("42/game-history,",
                    string.Empty));
                if (data[0].GetString() == "new-history")
                {
                    OnRainbetBet?.Invoke(this, data[1].Deserialize<RainbetWsBetModel>());
                    return;
                }
                _logger.Info($"Event {data[0].GetString()} from Rainbet was not handled");
                _logger.Info(message.Text);
                return;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Rainbet");
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