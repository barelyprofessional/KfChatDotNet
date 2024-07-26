using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetKickBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetKickBot.Services;

public class Howlgg : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://howl.gg/socket.io/?EIO=3&transport=websocket");
    // Howl will send its own timeout but seems it's always 30 seconds
    private int _reconnectTimeout = 30;
    private string? _proxy;
    public delegate void OnHowlggBetHistoryResponse(object sender, HowlggBetHistoryResponseModel data);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnHowlggBetHistoryResponse OnHowlggBetHistory;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private CancellationTokenSource _pingCts = new();
    private Task? _heartbeatTask;
    // Howl.gg tells us the heartbeat interval to use in the initial payload so this is just a placeholder
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(40);

    public Howlgg(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Howlgg WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://howl.gg");
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

    public void GetUserInfo(string userId)
    {
        var packet = "42/main,0[\"getUserInfo\",{\"userOrSteamId\":\"" + userId + "\",\"interval\":\"lifetime\"}]";
        _logger.Debug($"Sending packet: {packet}");
        _wsClient.Send(packet);
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
            _logger.Debug("Sending Howl.gg ping packet");
            _wsClient.Send("2");
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Howl.gg (or never successfully connected). Type is {disconnectionInfo.Type}");
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
            _logger.Info("Howl.gg sent a null message");
            return;
        }
        _logger.Trace($"Received event from Howl.gg: {message.Text}");

        try
        {
            var packetType = message.Text.Split('/')[0];
            if (packetType == "3")
            {
                _logger.Info("Received pong from Howl.gg");
                return;
            }

            // For some reason there's no / for the initial connection
            if (packetType.StartsWith("0{"))
            {
                // Received on initial connection
                var packetData = JsonSerializer.Deserialize<JsonElement>(message.Text.TrimStart('0'));
                _heartbeatInterval = TimeSpan.FromMilliseconds(packetData.GetProperty("pingInterval").GetInt32());
                _logger.Info("Received connection packet from Howl.gg. Setting up heartbeat timer");
                if (_heartbeatTask != null) return;
                _heartbeatTask = Task.Run(HeartbeatTimer, _cancellationToken);
                return;
            }

            if (message.Text == "40")
            {
                _logger.Trace("Ready to subscribe, sending main subscription");
                _wsClient.Send("40/main,");
                // To indicate successful subscription it echoes back the channel to you
                return;
            }

            if (packetType == "43")
            {
                // Bet History
                var jsonPayload = message.Text.Replace("43/main,0[null,", string.Empty).TrimEnd(']');
                var data = JsonSerializer.Deserialize<HowlggBetHistoryResponseModel>(jsonPayload);
                if (data != null) OnHowlggBetHistory?.Invoke(this, data);
                return;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Howl.gg");
            _logger.Error(e);
            _logger.Error("--- Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of Payload ---");
        }
    }

    public void Dispose()
    {
        _wsClient.Dispose();
        _pingCts.Cancel();
        _pingCts.Dispose();
        _heartbeatTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}