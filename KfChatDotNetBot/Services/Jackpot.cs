using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Jackpot : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient? _wsClient;
    private Uri _wsUri = new("wss://api.jackpot.bet/feeds/websocket");
    // Ping interval is 30 seconds
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnJackpotBetEventHandler(object sender, JackpotWsBetPayloadModel data);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnJackpotBetEventHandler? OnJackpotBet;
    public event OnWsDisconnectionEventHandler? OnWsDisconnection;
    private CancellationToken _cancellationToken;
    private CancellationTokenSource _pingCts = new();
    private Task? _heartbeatTask;
    // There's no smarts, it just does 30-second pings
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);

    public Jackpot(string? proxy = null, CancellationToken cancellationToken = default)
    {
        _proxy = proxy;
        _cancellationToken = cancellationToken;
        _logger.Info("Jackpot WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://jackpot.bet");
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

    private async Task HeartbeatTimer()
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);
        while (await timer.WaitForNextTickAsync(_pingCts.Token))
        {
            if (_wsClient == null)
            {
                _logger.Debug("_wsClient doesn't exist yet, not going to try ping");
                continue;
            }
            _logger.Debug("Sending Jackpot ping packet");
            _wsClient.Send("{\"id\":\"lfgkenokasino\",\"type\":\"ping\"}");
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Jackpot (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        if (reconnectionInfo.Type == ReconnectionType.Initial)
        {
            _logger.Debug("Sending initial payload");
            _wsClient?.Send("{\"id\":\"lfgkenokasinoinit\",\"type\":\"connection_init\"}");
        }
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Jackpot sent a null message");
            return;
        }
        _logger.Debug($"Received event from Jackpot: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<JackpotWsPacketModel>(message.Text);
            if (packet == null) throw new InvalidOperationException("Caught a null when deserializing Jackpot packet");
            if (packet.Type == "pong")
            {
                _logger.Info("Received pong from Jackpot");
                return;
            }

            if (packet.Type == "connection_ack")
            {
                _logger.Debug("Received ack from Jackpot. Sending subscription");
                _wsClient?.Send(
                    "{\"id\":\"lfgkenokasinoallbets\",\"type\":\"subscribe\",\"payload\":{\"feed\":\"all_bets\"}}\n");
                _logger.Debug("Setting up heartbeat timer");
                if (_heartbeatTask != null) return;
                _heartbeatTask = Task.Run(HeartbeatTimer, _cancellationToken);
                return;
            }

            if (packet.Type == "data")
            {
                _logger.Debug("Received bet from Jackpot");
                if (packet.Payload == null)
                    throw new InvalidOperationException("Payload can't be null when type is data");
                var data = packet.Payload.Value.Deserialize<JackpotWsBetPayloadModel>();
                if (data == null) throw new InvalidOperationException("Payload deserialized to a null");
                OnJackpotBet?.Invoke(this, data);
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Jackpot");
            _logger.Error(e);
            _logger.Error("--- Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of Payload ---");
        }
    }

    public async Task<JackpotQuickviewModel> GetJackpotUser(string username)
    {
        var url = $"https://api.jackpot.bet/user/quickview/{username}";
        _logger.Debug($"Formatted URL for quickview: {url}");
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.UseProxy = false;
            handler.Proxy = new WebProxy(_proxy);
            _logger.Debug($"Configured to use proxy {_proxy}");
        }

        using var client = new HttpClient(handler);
        var response = await client.GetAsync(url, _cancellationToken);
        var content = await response.Content.ReadFromJsonAsync<JackpotQuickviewModel>(_cancellationToken);
        if (content == null) throw new Exception("Failed to deserialize Jackpot quickview data");
        return content;
    }

    public void Dispose()
    {
        _wsClient?.Dispose();
        _pingCts.Cancel();
        _pingCts.Dispose();
        _heartbeatTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}