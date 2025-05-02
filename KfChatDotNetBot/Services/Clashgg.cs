using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Clashgg : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://ws.clash.gg/");
    // Ping interval is 30 seconds
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnClashBetEventHandler(object sender, ClashggBetModel data, JsonElement jsonElement);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnClashBetEventHandler OnClashBet;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private CancellationTokenSource _pingCts = new();
    private Task? _heartbeatTask;
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(60);
    private bool _isOnline = false;
    private DateTime _lastBet = DateTime.Now;
    // How long we can go without any gambling activity until we go 'fuck it' and reconnect
    private TimeSpan _lastBetTolerance = TimeSpan.FromMinutes(5);

    public Clashgg(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Clash.gg WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://clash.gg");
            clientWs.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:136.0) Gecko/20100101 Firefox/136.0");
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

    // This code was copied from Jackpot which has a ping function.
    // I haven't observed a ping feature for the ws.clash.gg endpoint
    // Therefore going to leave the code here in case it's needed but have it not do anything
    private async Task HeartbeatTimer()
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);
        while (await timer.WaitForNextTickAsync(_pingCts.Token))
        {
            if (_wsClient == null)
            {
                _logger.Debug("_wsClient doesn't exist yet, not going to do anything");
                continue;
            }

            if (DateTime.Now - _lastBet <= _lastBetTolerance) continue;
            _logger.Error("Forcing a disconnect of Clash.gg so the connection can be rebuilt " +
                          $"as there's been no gambling since {_lastBet:o}");
            await _wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Closed due to lack of gamba");
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Clash.gg (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        // No initial payload needs to be sent
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Clash.gg sent a null message");
            return;
        }
        _logger.Debug($"Received event from Clash.gg: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<List<JsonElement>>(message.Text);
            if (packet == null) throw new InvalidOperationException("Caught a null when deserializing Clash.gg packet");
            if (packet[0].GetString() == "online" && !_isOnline)
            {
                _logger.Info("Received online packet from Clash.gg. Subscribing to Plinko, Mines and Keno");
                _wsClient.Send("[\"subscribe\",\"plinko\"]");
                _wsClient.Send("[\"subscribe\",\"mines\"]");
                _wsClient.Send("[\"subscribe\",\"keno\"]");
                _isOnline = true;
                _heartbeatTask = Task.Run(HeartbeatTimer, _cancellationToken);
                return;
            }

            if (packet[0].GetString()!.Contains(":game"))
            {
                _lastBet = DateTime.Now;
            }

            if (packet[0].GetString() == "plinko:social-game")
            {
                _logger.Debug("Received Plinko game from Clash.gg. Deserializing payload");
                var betPacket = packet[1].Deserialize<ClashggWsPlinkoModel>();
                if (betPacket == null)
                {
                    throw new Exception("Caught a null when deserializing a Clash.gg Plinko packet");
                }
                var betData = new ClashggBetModel
                {
                    Game = ClashggGame.Plinko,
                    UserId = betPacket.UserId,
                    Username = "Unknown",
                    Bet = betPacket.BetAmount,
                    Currency = betPacket.Currency == "REAL" ? ClashggCurrency.Real : ClashggCurrency.Fake,
                    Multiplier = betPacket.Multiplier,
                    Payout = betPacket.BetAmount * betPacket.Multiplier
                };
                OnClashBet?.Invoke(this, betData, packet[1]);
                return;
            }
            
            if (packet[0].GetString() == "mines:game")
            {
                _logger.Debug("Received Mines game from Clash.gg. Deserializing payload");
                var betPacket = packet[1].Deserialize<ClashggWsMinesModel>();
                if (betPacket == null)
                {
                    throw new Exception("Caught a null when deserializing a Clash.gg Mines packet");
                }
                var betData = new ClashggBetModel
                {
                    Game = ClashggGame.Mines,
                    UserId = betPacket.User.Id,
                    Username = betPacket.User.Name,
                    Bet = betPacket.BetAmount,
                    Currency = betPacket.Currency == "REAL" ? ClashggCurrency.Real : ClashggCurrency.Fake,
                    Multiplier = (float)betPacket.Payout / betPacket.BetAmount,
                    Payout = betPacket.Payout
                };
                OnClashBet?.Invoke(this, betData, packet[1]);
                return;
            }
            
            if (packet[0].GetString() == "keno:game")
            {
                _logger.Debug("Received Keno game from Clash.gg. Deserializing payload");
                var betPacket = packet[1].Deserialize<ClashggWsKenoModel>();
                if (betPacket == null)
                {
                    throw new Exception("Caught a null when deserializing a Clash.gg Keno packet");
                }
                var betData = new ClashggBetModel
                {
                    Game = ClashggGame.Keno,
                    UserId = betPacket.User.Id,
                    Username = betPacket.User.Name,
                    Bet = betPacket.BetAmount,
                    Currency = betPacket.Currency == "REAL" ? ClashggCurrency.Real : ClashggCurrency.Fake,
                    Multiplier = betPacket.Multiplier,
                    Payout = betPacket.Payout
                };
                OnClashBet?.Invoke(this, betData, packet[1]);
                return;
            }
            
            _logger.Debug($"Message of type '{packet[0].GetString()}' from Clash.gg not handled");
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Clash.gg");
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