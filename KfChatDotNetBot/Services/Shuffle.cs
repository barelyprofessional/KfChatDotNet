using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Shuffle : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://subscription-temp.shuffle.com/graphql");
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnLatestBetUpdatedEventHandler(object sender, ShuffleLatestBetModel bet);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnLatestBetUpdatedEventHandler OnLatestBetUpdated;
    public event OnWsDisconnectionEventHandler OnWsDisconnection;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private CancellationTokenSource _pingCts = new();
    private Task _pingTask;

    public Shuffle(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        // Moved it up here as I'm concerned about the possibility of reconnections creating multiple ping tasks
        _pingTask = PeriodicPing();
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
            clientWs.Options.AddSubProtocol("graphql-transport-ws");
            clientWs.Options.SetRequestHeader("Origin", "https://shuffle.com");
            clientWs.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0");
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout),
            IsReconnectionEnabled = false // Watchdog will self-destruct this instead
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

    private async Task PeriodicPing()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_pingCts.Token))
        {
            if (_wsClient == null)
            {
                _logger.Debug("_wsClient doesn't exist yet, not going to try ping");
                continue;
            }
            _logger.Debug("Sending ping to Shuffle");
            _wsClient.Send("{\"type\":\"ping\"}");
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Shuffle (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        _logger.Info("Sending connection_init");
        var initPayload =
            "{\"type\":\"connection_init\",\"payload\":{\"x-correlation-id\":\"pdvlnd9tej-di27abvq19-1.30.2-1i0nef1m7-g::anon\",\"authorization\":\"\"}}";
        _logger.Debug(initPayload);
        _wsClient.Send(initPayload);
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Shuffle sent a null message");
            return;
        }
        _logger.Trace($"Received event from Shuffle: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<JsonElement>(message.Text);
            var packetType = packet.GetProperty("type").GetString();

            if (packetType == "connection_ack")
            {
                _logger.Debug("connection_ack packet, sending subscribe payload");
                _logger.Info("Sending subscription request");
                // we're super ghetto today
                var payload = "{\"id\":\"" + Guid.NewGuid() +
                              "\",\"type\":\"subscribe\",\"payload\":{\"variables\":{},\"extensions\":{},\"operationName\":\"LatestBetUpdated\",\"query\":\"subscription LatestBetUpdated {\\n  latestBetUpdated {\\n    ...BetActivityFields\\n    __typename\\n  }\\n}\\n\\nfragment BetActivityFields on BetActivityPayload {\\n  id\\n  username\\n  vipLevel\\n  currency\\n  amount\\n  payout\\n  multiplier\\n  gameName\\n  gameCategory\\n  gameSlug\\n  __typename\\n}\"}}";
                _logger.Debug(payload);
                _wsClient.SendInstant(payload).Wait(_cancellationToken);
                return;
            }

            if (packetType == "pong")
            {
                _logger.Info("Shuffle pong packet");
                return;
            }

            // GAMBA
            if (packetType == "next")
            {
                var bet = packet.GetProperty("payload").GetProperty("data").GetProperty("latestBetUpdated")
                    .Deserialize<ShuffleLatestBetModel>();
                if (bet == null)
                {
                    _logger.Error("Caught a null before invoking bet event");
                    throw new NullReferenceException("Caught a null before invoking bet event");
                }
                OnLatestBetUpdated?.Invoke(this, bet);
                return;
            }
            _logger.Info("Message from Shuffle was unhandled");
            _logger.Info(message.Text);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Shuffle");
            _logger.Error(e);
            _logger.Error("--- JSON Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of JSON Payload ---");
        }
    }

    public async Task<ShuffleUserModel> GetShuffleUser(string username)
    {
        var graphQl =
            "query GetUserProfile($username: String!) {\n  user(username: $username) {\n    id\n    username\n    vipLevel\n    createdAt\n    avatar\n    avatarBackground\n    bets\n    usdWagered\n    __typename\n  }\n}";
        _logger.Debug($"Grabbing details for Shuffle user {username}");
        var jsonBody = new Dictionary<string, object>
        {
            { "operationName", "GetUserProfile" },
            { "query", graphQl },
            { "variables", new Dictionary<string, string> { { "username", username } } }
        };
        _logger.Debug("Created dictionary object for the JSON payload, should serialize to following value:");
        _logger.Debug(JsonSerializer.Serialize(jsonBody));
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.UseProxy = false;
            handler.Proxy = new WebProxy(_proxy);
            _logger.Debug($"Configured to use proxy {_proxy}");
        }
        
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("content-type", "application/json");
        var postBody = JsonContent.Create(jsonBody);
        var response = await client.PostAsync("https://shuffle.com/graphql", postBody, _cancellationToken);
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Shuffle returned following JSON");
        _logger.Debug(responseContent.GetRawText);
        var user = responseContent.GetProperty("data").GetProperty("user");
        if (user.ValueKind == JsonValueKind.Null)
        {
            _logger.Debug("data.user was null");
            throw new ShuffleUserNotFoundException();
        }

        return user.Deserialize<ShuffleUserModel>() ?? throw new InvalidOperationException();
    }

    public class ShuffleUserNotFoundException : Exception;

    public void Dispose()
    {
        _wsClient.Dispose();
        // Rare bug but has happened at least once
        try
        {
            _pingCts.Cancel();
        }
        catch (ObjectDisposedException e)
        {
            _logger.Error("Caught object disposed exception when trying to send a cancellation to the ping task");
            _logger.Error(e);
        }
        _pingCts.Dispose();
        _pingTask.Dispose();
        GC.SuppressFinalize(this);
    }
}