using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Winna : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient? _wsClient;
    private Uri _wsUri = new("wss://games-content-prod.winna.com/ws/?EIO=4&transport=websocket");
    private int _reconnectTimeout = 30;
    private string? _proxy;
    public delegate void OnWinnaBetEventHandler(object sender, WinnaBetModel bet);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnWinnaBetEventHandler? OnWinnaBet;
    public event OnWsDisconnectionEventHandler? OnWsDisconnection;
    private string? _userAgent;
    private CookieContainer _cookieContainer = new();

    public Winna(string? proxy = null)
    {
        _proxy = proxy;
        _logger.Info("Winna WebSocket client created");
    }

    public async Task StartWsClient()
    {
        _logger.Debug("StartWsClient() called, creating client");
        await CreateWsClient();
    }

    public async Task PopulateCookieContainer()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = _cookieContainer,
            UseCookies = true
        };
        if (_proxy != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(_proxy);
        }

        using var client = new HttpClient(handler);
        var response =
            await client.GetAsync("https://games-content-prod.winna.com/public/v1/feeds?type=allBets&perPage=6");
        _ = await response.Content.ReadAsStringAsync();
    }

    private async Task CreateWsClient()
    {
        await PopulateCookieContainer();
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            clientWs.Options.SetRequestHeader("Origin", "https://winna.com");
            clientWs.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0");
            clientWs.Options.Cookies = _cookieContainer;
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
        _logger.Error($"Client disconnected from Winna (or never successfully connected). Type is {disconnectionInfo.Type}");
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
            _logger.Info("Winna sent a null message");
            return;
        }
        _logger.Trace($"Received event from Winna: {message.Text}");

        try
        {
            if (message.Text.StartsWith("0{"))
            {
                // 0{"sid":"5Q2cD9HxTUT3Kx-pAMMW","upgrades":[],"pingInterval":25000,"pingTimeout":20000,"maxPayload":1000000}
                _logger.Info("Received initial connection message from Winna, sending subscribe");
                _wsClient?.Send("42[\"feed.subscribe\",{\"feedType\":\"allBets\"}]");
                return;
            }
            var packetType = message.Text.Split('/')[0];
            if (packetType == "2")
            {
                _logger.Info("Received ping from Winna, replying with pong");
                _wsClient?.Send("3");
                return;
            }
            
            if (packetType == "42")
            {
                var data = JsonSerializer.Deserialize<List<JsonElement>>(message.Text[..2]);
                if (data == null) throw new Exception("Caught a null when deserializing feed.update");
                if (data[0].GetString() == "feed.update")
                {
                    var isSnapshot = data[1].TryGetProperty("snapshot", out _);
                    if (isSnapshot)
                    {
                        _logger.Info("Received a bet history snapshot from Winna, ignoring");
                        return;
                    }

                    var item = data[1].GetProperty("item").Deserialize<WinnaBetModel>();
                    if (item == null)
                    {
                        throw new Exception("Caught a null when deserializing feed.update item");
                    }
                    OnWinnaBet?.Invoke(this, item);
                    return;
                }
                _logger.Info($"Event {data[0].GetString()} from Winna was not handled");
                _logger.Info(message.Text);
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Winna");
            _logger.Error(e);
            _logger.Error("--- Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of Payload ---");
        }
    }

    public void Dispose()
    {
        _wsClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}