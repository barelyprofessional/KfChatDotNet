using System.Net;
using System.Net.Http.Headers;
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
    private WebsocketClient? _wsClient;
    private Uri _wsUri = new("wss://shuffle.com/main-api/bp-subscription/subscription/graphql");
    private int _reconnectTimeout = 60;
    private string? _proxy;
    public delegate void OnLatestBetUpdatedEventHandler(object sender, ShuffleLatestBetModel bet, bool isDotUs);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnLatestBetUpdatedEventHandler? OnLatestBetUpdated;
    public event OnWsDisconnectionEventHandler? OnWsDisconnection;
    private CancellationToken _cancellationToken;
    private CancellationTokenSource _pingCts = new();
    private Task _pingTask;

    public Shuffle(string? proxy = null, CancellationToken cancellationToken = default)
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
        _wsClient?.Send(initPayload);
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
                _wsClient?.SendInstant(payload).Wait(_cancellationToken);
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
                OnLatestBetUpdated?.Invoke(this, bet, false);
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
        client.DefaultRequestHeaders.Referrer = new Uri($"https://shuffle.com/?md-id={betId}&modal=bet");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://shuffle.com");
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (X11; Linux x86_64; rv:147.0) Gecko/20100101 Firefox/147.0");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.TryParseAdd("en-US");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/graphql-response+json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var postBody = JsonContent.Create(jsonBody, new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await client.PostAsync("https://shuffle.com/main-api/graphql/api/graphql", postBody, _cancellationToken);
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Shuffle returned following JSON");
        _logger.Debug(responseContent.GetRawText);
        /*
         * {
               "data": {
                   "bet": {
                       "id": "CYjG9Hdq9fi8wwWUkXC8c",
                       "completedAt": "2026-03-05T03:57:43.907Z",
                       "account": {
                           "id": "c09e7697-a9b6-409e-be6a-bc5a17321c2f",
                           "user": {
                               "username": null,
                               "vipLevel": "PLATINUM_1",
                               "__typename": "User"
                           },
                           "__typename": "Account"
                       },
                       "game": {
                           "id": "36f0d698-178d-4745-b2bf-d8c99e156683",
                           "name": "Dice",
                           "slug": "originals/dice",
                           "edge": "1",
                           "accentColor": "#05D550",
                           "image": {
                               "key": "437b7bc5-ded9-4555-ae0d-5e21cd272291",
                               "__typename": "Image"
                           },
                           "gameAndGameCategories": [
                               {
                                   "gameCategoryName": "ORIGINALS",
                                   "gameId": "36f0d698-178d-4745-b2bf-d8c99e156683",
                                   "main": true,
                                   "__typename": "GameAndGameCategory"
                               }
                           ],
                           "provider": {
                               "id": "original",
                               "name": "Shuffle Games",
                               "__typename": "GameProvider"
                           },
                           "originalGame": "DICE",
                           "__typename": "Game"
                       },
                       "gameSeed": {
                           "id": "b817d7d1-8aa0-444a-84f7-bcfba1e75782",
                           "clientSeed": "jvq0tmv0rb",
                           "seed": null,
                           "hashedSeed": "4d9de6aff5329de97adeaee907552ab8bfc4a99bb59520840eefe66ffa88e919",
                           "status": "ACTIVE",
                           "currentNonce": "97",
                           "createdAt": "2026-02-27T05:46:25.297Z",
                           "__typename": "GameSeed"
                       },
                       "gameSeedNonce": 97,
                       "shuffleOriginalActions": [
                           {
                               "id": "019cbc24-f263-74df-9b53-50d5b39be8c5",
                               "updatedAt": "2026-03-05T03:57:43.903Z",
                               "createdAt": "2026-03-05T03:57:43.903Z",
                               "action": {
                                   "dice": {
                                       "userDiceDirection": "ABOVE",
                                       "userValue": "50.5",
                                       "resultValue": "76.97",
                                       "resultRaw": "c508741181297b0f2282d16477fc833a8dcf6415724e1b24b8bec00b83a98539",
                                       "__typename": "DiceActionModel"
                                   },
                                   "plinko": null,
                                   "mines": null,
                                   "limbo": null,
                                   "keno": null,
                                   "hilo": null,
                                   "blackjack": null,
                                   "roulette": null,
                                   "wheel": null,
                                   "tower": null,
                                   "chicken": null,
                                   "__typename": "ShuffleOriginalActionModel"
                               },
                               "__typename": "ShuffleOriginalAction"
                           }
                       ],
                       "amount": "40",
                       "originalAmount": "40",
                       "payout": "80",
                       "currency": "USDC",
                       "usdRate": "1",
                       "createdAt": "2026-03-05T03:57:43.903Z",
                       "afterBalance": null,
                       "multiplier": 2,
                       "replayUrl": null,
                       "__typename": "Bet"
                   }
               }
           }
         */
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