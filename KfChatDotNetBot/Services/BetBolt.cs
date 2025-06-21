using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class BetBolt : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://betbolt.io/api/ws");
    // Pings every 5 seconds so 15 seconds should be reasonable
    private int _reconnectTimeout = 15;
    private string? _proxy;
    public delegate void OnBetBoltBetEventHandler(object sender, BetBoltBetModel bet);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnBetBoltBetEventHandler OnBetBoltBet;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;
    public BetBolt(string? proxy = null, CancellationToken? cancellationToken = null)
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
            clientWs.Options.SetRequestHeader("Origin", "https://betbolt.io");
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
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from BetBolt (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        if (reconnectionInfo.Type == ReconnectionType.Initial)
        {
            _logger.Info("Sending subscribe payload to BetBolt");
            _wsClient.Send("{\"topic\":\"system/EN\",\"action\":\"subscribe\"}");

        }
    }

    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("JeetBolt sent us null message, ignoring");
            return;
        }
        _logger.Debug($"Received event from BetBolt: {message.Text}");
        try
        {
            var packet = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.Text);
            if (packet == null) throw new InvalidOperationException("Caught a null when deserializing BetBolt packet");
            if (packet.ContainsKey("success"))
            {
                if (packet["success"].GetBoolean())
                {
                    _logger.Info("Successfully connected to BetBolt");
                    return;
                }
                _logger.Error("BetBolt says our connection wasn't successful, JSON payload follows");
                _logger.Error(message.Text);
                return;
            }

            if (packet.ContainsKey("ping"))
            {
                _logger.Info("Received ping from BetBolt");
                return;
            }

            if (packet.ContainsKey("topic") && packet["topic"].GetString() == "new-bet-event")
            {
                _logger.Debug("Received a bet event from BetBolt");
                var betPayload = JsonSerializer.Deserialize<BetBoltBetPayloadModel>(message.Text);
                if (betPayload == null)
                    throw new InvalidOperationException("Failed to deserialize bet payload for BetBolt");
                if (betPayload.Username == null)
                    throw new InvalidOperationException("Username in BetBolt bet payload was null");
                var bet = new BetBoltBetModel
                {
                    Username = betPayload.Username,
                    Time = betPayload.Time,
                    GameName = betPayload.GameName,
                    Crypto = betPayload.CryptoCode,
                    BetAmountFiat = double.Parse(betPayload.BetAmountFiat),
                    BetAmountCrypto = double.Parse(betPayload.BetAmountCrypto),
                    WinAmountCrypto = double.Parse(betPayload.WinAmountCrypto),
                    WinAmountFiat = double.Parse(betPayload.WinAmountFiat),
                    Multiplier = double.Parse(betPayload.Multiplier ?? "0"),
                    Payload = betPayload
                };
                OnBetBoltBet?.Invoke(this, bet);
                return;
            }
            _logger.Debug("Unhandled event from BetBolt. Payload follows");
            _logger.Debug(message.Text);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from BetBolt");
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