using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using KfChatDotNetBot.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class Chipsgg : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://api.chips.gg/prod/socket");
    // Chips doesn't have a heartbeat packet
    private int _reconnectTimeout = 30;
    private string? _proxy;
    public delegate void OnChipsggRecentBetEventHandler(object sender, ChipsggBetModel bet);
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event OnChipsggRecentBetEventHandler OnChipsggRecentBet;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private Dictionary<string, ChipsggCurrencyModel> _currencies = new();

    public Chipsgg(string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Chipsgg WebSocket client created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://chips.gg");
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
        _logger.Error($"Client disconnected from Howl.gg (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        if (reconnectionInfo.Type == ReconnectionType.Initial)
        {
            _logger.Info("Sending auth payload to Chips.gg");
            _wsClient.Send("[\"auth\",1,\"token\",[]]");
        }
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Chips.gg sent a null message");
            return;
        }
        _logger.Trace($"Received event from Chips.gg: {message.Text}");

        try
        {
            // Chipsgg has literally the most retarded "structure" to their packets I have ever seen
            // I was hoping BMJ would dump their lousy site, but it hasn't happened yet
            // Everything is arrays within arrays.
            // For bets, each element of the outer array is a property related to the bet
            // For auth / currency stuff there's only one element which contains more arrays
            var payload = JsonSerializer.Deserialize<List<JsonElement>>(message.Text);
            if (payload == null || payload.Count == 0)
            {
                throw new Exception("Chips.gg sent us an empty array or I could not deserialize it");
            }

            var firstElement = payload[0].Deserialize<List<JsonElement>>();
            if (firstElement == null || firstElement.Count < 3)
            {
                throw new Exception("Chips.gg's first element was smaller than expected or null");
            }

            if (firstElement[0].GetString() == "auth")
            {
                if (firstElement[1].GetInt32() == 1)
                {
                    var guid = firstElement[2].GetString();
                    _logger.Debug("Received auth packet, sending back GUID auth with " + guid);
                    _wsClient.Send("[\"auth\",2,\"authenticate\",[\"" + guid+ "\"]]");
                    return;
                }

                if (firstElement[1].GetInt32() == 2)
                {
                    _logger.Info("Chips.gg responded to our auth with: " + firstElement[2].GetString());
                    _logger.Info("Sending Chips.gg recent bets subscription packet");
                    _wsClient.Send("[\"stats\",12,\"on\",[{\"game\":\"bets\",\"type\":\"recentBets\"}]]");
                    return;
                }

                throw new Exception("Auth packet was unhandled");
            }

            // First packet after auth is a currency + settings payload
            // This will also match for periodic currency updates
            if (firstElement[0].GetString() == "public")
            {
                var dataElement = firstElement[2].Deserialize<List<JsonElement>>();
                if (dataElement == null || dataElement.Count < 2)
                {
                    throw new Exception(
                        "Caught a null when grabbing data from the first element of the array or got fewer items than expected");
                }

                var path = dataElement[0].Deserialize<List<string>>();
                if (path == null)
                    throw new Exception("Caught a null when deserializing the path element of the array");
                if (path.Count == 0)
                {
                    _logger.Debug("Received initial currency payload as the path array was empty");
                    var currencyData = dataElement[1].Deserialize<Dictionary<string, JsonElement>>();
                    if (currencyData == null) throw new Exception("Caught a null when deserializing currency data");
                    if (!currencyData.TryGetValue("currencies", out var val)) throw new Exception("Currency object didn't contain expected currencies property");
                    var currencies = val.Deserialize<Dictionary<string, JsonElement>>();
                    if (currencies == null) throw new Exception("Caught a null when deserializing currency dictionary");
                    foreach (var currency in currencies.Keys)
                    {
                        // Should never happen but you never know
                        if (_currencies.ContainsKey(currency)) return;
                        float? price = null;
                        // Where a price is not set, the element is simply missing
                        if (currencies[currency].TryGetProperty("price", out var priceElement))
                        {
                            price = priceElement.GetSingle();
                        }
                        _currencies.Add(currency, new ChipsggCurrencyModel
                        {
                            Decimals = currencies[currency].GetProperty("decimals").GetInt32(),
                            Name = currency,
                            // Hidden is only present when it's true
                            Hidden = currencies[currency].TryGetProperty("hidden", out _),
                            Price = price
                        });
                        _logger.Debug($"Ingested currency data for {currency}");
                    }
                    return;
                }

                foreach (var element in payload)
                {
                    var data = element.Deserialize<List<JsonElement>>();
                    if (data == null || data.Count < 3) throw new Exception("Caught null or received fewer than 3 elements in the data array");
                    var innerData = data[2].Deserialize<List<JsonElement>>();
                    if (innerData == null || data.Count < 2) throw new Exception("Caught null or received fewer than 2 elements in the inner data array");
                    var innerDataPath = innerData[0].Deserialize<List<string>>();
                    if (innerDataPath == null || innerDataPath.Count == 0) throw new Exception("innerDataPath was null or contained no elements");
                    if (innerDataPath.Contains("metrics")) continue;
                    // No idea with koth is, so we'll ignore it
                    if (innerDataPath[0] == "koth") return;
                    var currency = innerDataPath[1];
                    if (_currencies.TryGetValue(currency, out var updatedCurrency))
                    {
                        updatedCurrency.Price = innerData[1].GetSingle();
                    }
                    _logger.Debug($"Updated currency data for {currency}");
                }

                return;
            }

            if (firstElement[0].GetString() == "stats")
            {
                if (firstElement[1].ValueKind == JsonValueKind.Number && firstElement[1].TryGetInt32(out var type))
                {
                    // 12 is the replay of recent bets
                    if (type == 12) return;
                }
                // Currency data may not be known until after so hold it here til we're done parsing
                var amount = string.Empty;
                var winnings = string.Empty;
                var bet = new ChipsggBetModel();
                foreach (var element in payload)
                {
                    var data = element.Deserialize<List<JsonElement>>();
                    if (data == null || data.Count < 3) throw new Exception("Caught null or received fewer than 3 elements in the data array");
                    var innerData = data[2].Deserialize<List<JsonElement>>();
                    if (innerData == null || data.Count < 2) throw new Exception("Caught null or received fewer than 2 elements in the inner data array");
                    var innerDataPath = innerData[0].Deserialize<List<string>>();
                    if (innerDataPath == null || innerDataPath.Count == 0) throw new Exception("innerDataPath was null or contained no elements");
                    var innerDataPathJoined = string.Join(':', innerDataPath);
                    // For some reason there are ghostly bets sent alongside real bets whose values are all null
                    if (innerData[1].ValueKind == JsonValueKind.Null) continue;
                    if (innerDataPathJoined.EndsWith("bet:done"))
                    {
                        if (innerData[1].GetBoolean() == false)
                        {
                            _logger.Debug("Bet not yet complete, ignoring");
                            return;
                        }
                        continue;
                    }
                    
                    // Just piggybacking it for a reliable path to grab the bet ID from
                    if (innerDataPathJoined.EndsWith("bet:created"))
                    {
                        bet.BetId = innerDataPath[2];
                        bet.Created = DateTimeOffset.FromUnixTimeMilliseconds(innerData[1].GetInt64());
                        continue;
                    }

                    if (innerDataPathJoined.EndsWith("bet:updated"))
                    {
                        bet.Updated = DateTimeOffset.FromUnixTimeMilliseconds(innerData[1].GetInt64());
                        continue;
                    }

                    if (innerDataPathJoined.EndsWith("bet:userid"))
                    {
                        bet.UserId = innerData[1].GetString()!;
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("player:username"))
                    {
                        bet.Username = innerData[1].GetString()!;
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("bet:win"))
                    {
                        bet.Win = innerData[1].GetBoolean();
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("bet:winnings"))
                    {
                        winnings = innerData[1].GetString()!;
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("game:title"))
                    {
                        bet.GameTitle = innerData[1].GetString()!;
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("bet:amount"))
                    {
                        amount = innerData[1].GetString()!;
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("bet:multiplier"))
                    {
                        bet.Multiplier = innerData[1].GetSingle();
                        continue;
                    }
                    
                    if (innerDataPathJoined.EndsWith("bet:currency"))
                    {
                        bet.Currency = innerData[1].GetString()!;
                    }
                }

                // Just something that randomly happens where incomplete bets are sent
                // It seems that occasionally a bet is sent through with no proper game title or username
                // Since the feed in theory can't display these, I'm assuming it's another ghost and not a real bet
                if (bet.Currency == null || bet.GameTitle == null) return;
                if (!_currencies.TryGetValue(bet.Currency, out var currencyData))
                {
                    throw new Exception($"Unknown currency {bet.Currency}");
                }

                // Another mysterious thing where winnings are sometimes sent and sometimes not. Presumed to be 0
                if (winnings == string.Empty) winnings = "0";
                bet.Winnings = double.Parse(winnings) / double.Parse(1.ToString().PadRight(currencyData.Decimals + 1, '0'));
                bet.Amount = double.Parse(amount) / double.Parse(1.ToString().PadRight(currencyData.Decimals + 1, '0'));
                bet.CurrencyPrice = currencyData.Price ?? 0;
                OnChipsggRecentBet?.Invoke(this, bet);
                return;
            }
            _logger.Debug("Unhandled event from Chips.gg");
            _logger.Debug(message.Text);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Chips.gg");
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