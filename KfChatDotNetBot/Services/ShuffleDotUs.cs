using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class ShuffleDotUs : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient? _wsClient;
    private Uri _wsUri = new("wss://shuffle.us/main-api/bp-subscription/subscription/graphql");
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnLatestBetUpdatedEventHandler(object sender, ShuffleLatestBetModel bet, bool isDotUs);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnLatestBetUpdatedEventHandler? OnLatestBetUpdated;
    public event OnWsDisconnectionEventHandler? OnWsDisconnection;
    private CancellationToken _cancellationToken;
    private CancellationTokenSource _pingCts = new();
    private Task _pingTask;

    public ShuffleDotUs(string? proxy = null, CancellationToken cancellationToken = default)
    {
        _proxy = proxy;
        _cancellationToken = cancellationToken;
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
            clientWs.Options.SetRequestHeader("Origin", "https://shuffle.us");
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
            _logger.Debug("Sending ping to Shuffle.us");
            _wsClient.Send("{\"type\":\"ping\"}");
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Shuffle.us (or never successfully connected). Type is {disconnectionInfo.Type}");
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
        _wsClient?.Send(initPayload);
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Shuffle.us sent a null message");
            return;
        }
        _logger.Trace($"Received event from Shuffle.us: {message.Text}");

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
                _wsClient?.SendInstant(payload).Wait(_cancellationToken);
                return;
            }

            if (packetType == "pong")
            {
                _logger.Info("Shuffle.us pong packet");
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
                OnLatestBetUpdated?.Invoke(this, bet, true);
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
    
        public async Task<string> GetBetUser(string betId)
    {
        var gql = "query GetBetInfo($betId: String!) {\n  bet(id: $betId) {\n    id\n    completedAt\n    account {\n      id\n      user {\n        username\n        vipLevel\n        __typename\n      }\n      __typename\n    }\n    game {\n      id\n      name\n      slug\n      edge\n      accentColor\n      image {\n        key\n        __typename\n      }\n      gameAndGameCategories {\n        gameCategoryName\n        gameId\n        main\n        __typename\n      }\n      provider {\n        id\n        name\n        __typename\n      }\n      originalGame\n      __typename\n    }\n    gameSeed {\n      ...GameSeedFields\n      __typename\n    }\n    gameSeedNonce\n    shuffleOriginalActions {\n      id\n      updatedAt\n      createdAt\n      action {\n        dice {\n          ...DiceFields\n          __typename\n        }\n        plinko {\n          multiplier\n          results\n          risk\n          rows\n          __typename\n        }\n        mines {\n          minesResult\n          minesCount\n          winMultiplier\n          selected\n          __typename\n        }\n        limbo {\n          resultRaw\n          resultValue\n          userValue\n          __typename\n        }\n        keno {\n          results\n          risk\n          multiplier\n          selected\n          __typename\n        }\n        hilo {\n          card\n          guess\n          winMultiplier\n          actionType\n          __typename\n        }\n        blackjack {\n          mainPlayerHand\n          mainPlayerActions\n          splitPlayerHand\n          splitPlayerActions\n          dealerHand\n          perfectPairWin\n          twentyOnePlusThreeWin\n          twentyOnePlusThreeAmount\n          perfectPairAmount\n          insuranceStatus\n          originalMainBetAmount\n          mainHandOutcome\n          splitHandOutcome\n          __typename\n        }\n        roulette {\n          resultRaw\n          resultValue\n          userInput {\n            parityValues {\n              amount\n              parity\n              __typename\n            }\n            colorValues {\n              amount\n              color\n              __typename\n            }\n            halfValues {\n              amount\n              half\n              __typename\n            }\n            columnValues {\n              amount\n              column\n              __typename\n            }\n            dozenValues {\n              amount\n              dozen\n              __typename\n            }\n            straightValues {\n              amount\n              straightNumber\n              __typename\n            }\n            splitValues {\n              amount\n              firstNumber\n              secondNumber\n              __typename\n            }\n            streetValues {\n              amount\n              street\n              __typename\n            }\n            cornerValues {\n              amount\n              firstNumber\n              secondNumber\n              thirdNumber\n              fourthNumber\n              __typename\n            }\n            doubleStreetValues {\n              amount\n              firstStreet\n              secondStreet\n              __typename\n            }\n            __typename\n          }\n          __typename\n        }\n        wheel {\n          resultRaw\n          resultSegment\n          risk\n          segments\n          __typename\n        }\n        tower {\n          towerResult\n          towerDifficulty\n          winMultiplier\n          selected\n          __typename\n        }\n        chicken {\n          chickenResult\n          chickenDifficulty\n          winMultiplier\n          selectedLane\n          __typename\n        }\n        __typename\n      }\n      __typename\n    }\n    amount\n    originalAmount\n    payout\n    currency\n    usdRate\n    createdAt\n    afterBalance\n    multiplier\n    replayUrl\n    __typename\n  }\n}\n\nfragment GameSeedFields on GameSeed {\n  id\n  clientSeed\n  seed\n  hashedSeed\n  status\n  currentNonce\n  createdAt\n  __typename\n}\n\nfragment DiceFields on DiceActionModel {\n  userDiceDirection\n  userValue\n  resultValue\n  resultRaw\n  __typename\n}";
        _logger.Debug($"Grabbing details for Shuffle bet {betId}");
        var jsonBody = new Dictionary<string, object>
        {
            { "operationName", "GetBetInfo" },
            { "query", gql },
            { "variables", new Dictionary<string, string> { { "betId", betId } } },
            { "extensions", new Dictionary<string, Dictionary<string, string>> {
            {
                "clientLibrary", new Dictionary<string, string>()
                {
                    {"name", "@apollo/client"},
                    {"version", "4.1.4"}
                }
            } } },
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
        client.DefaultRequestHeaders.Referrer = new Uri($"https://shuffle.us/?md-id={betId}&modal=bet");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://shuffle.us");
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (X11; Linux x86_64; rv:147.0) Gecko/20100101 Firefox/147.0");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.TryParseAdd("en-US");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/graphql-response+json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var postBody = JsonContent.Create(jsonBody, new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await client.PostAsync("https://shuffle.us/main-api/graphql/api/graphql", postBody, _cancellationToken);
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Shuffle returned following JSON");
        _logger.Debug(responseContent.GetRawText);
        var user = responseContent.GetProperty("data").GetProperty("bet").GetProperty("account").GetProperty("id");
        if (user.ValueKind == JsonValueKind.Null)
        {
            _logger.Debug("user was null");
            throw new ShuffleUserNotFoundException();
        }

        return user.GetString() ?? throw new InvalidOperationException();
    }

    public async Task<ShuffleUserModel> GetShuffleUser(string username)
    {
        var graphQl =
            "query GetUserProfile($username: String!) {\n  user(username: $username) {\n    id\n    username\n    vipLevel\n    createdAt\n    avatar\n    avatarBackground\n    bets\n    usdWagered\n    __typename\n  }\n}";
        _logger.Debug($"Grabbing details for Shuffle.us user {username}");
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
        var response = await client.PostAsync("https://shuffle.us/graphql", postBody, _cancellationToken);
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Shuffle.us returned following JSON");
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
        _wsClient?.Dispose();
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