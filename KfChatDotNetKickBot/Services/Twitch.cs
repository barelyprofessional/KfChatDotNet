using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using NLog;
using Websocket.Client;

namespace KfChatDotNetKickBot.Services;

public class Twitch
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient _wsClient;
    private Uri _wsUri = new("wss://pubsub-edge.twitch.tv/v1");
    private int _reconnectTimeout = 300;
    private string? _proxy;
    private List<int> _channels;
    public delegate void OnStreamStateUpdateEventHandler(object sender, int channelId, bool isLive);
    public event OnStreamStateUpdateEventHandler OnStreamStateUpdated;
    private CancellationToken _cancellationToken = CancellationToken.None;

    public Twitch(List<int> channels,  string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _proxy = proxy;
        _channels = channels;
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
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
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(_reconnectTimeout)
        };
        
        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _wsClient = client;
        
        _logger.Debug("Websocket client has been built, about to start");
        await client.Start();
        _logger.Debug("Websocket client started!");
        SendPing();
        _ = PeriodicPing();
    }
    
    public bool IsConnected()
    {
        return _wsClient is { IsRunning: true };
    }

    private void SendPing()
    {
        _logger.Debug("Sending ping to Twitch");
        _wsClient.Send("{\"type\":\"PING\"}");
    }

    private async Task PeriodicPing()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            SendPing();
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from the chat (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error(disconnectionInfo.Exception);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        _logger.Info("Sending subscription requests");
        foreach (var channel in _channels)
        {
            _logger.Info($"Subscribing to {channel}");
            var payload = "{\"data\":{\"topics\":[\"video-playback-by-id." + channel + "\"]},\"nonce\":\"" +
                          Guid.NewGuid() + "\",\"type\":\"LISTEN\"}";
            _logger.Debug("Sending the following JSON to Twitch");
            _logger.Debug(payload);
            _wsClient.Send(payload);
        }
    }

    // Stream start JSON
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"server_time\":1718631487,\"play_delay\":0,\"type\":\"stream-up\"}"}}
    // View count update (every 30 seconds)
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631500.636146,\"viewers\":62}"}}
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631530.654308,\"viewers\":162}"}}
    // {"type":"MESSAGE","data":{"topic":"video-playback-by-id.114122847","message":"{\"type\":\"viewcount\",\"server_time\":1718631560.551188,\"viewers\":179}"}}
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Twitch sent a null message");
            return;
        }
        _logger.Debug($"Received event from Twitch: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<JsonElement>(message.Text);
            if (packet.GetProperty("type").GetString() != "MESSAGE")
                return;
            var data = packet.GetProperty("data")!;
            var topicString = data.GetProperty("topic")!.GetString()!;
            if (!topicString.StartsWith("video-playback-by-id."))
                return;
            var topicParts = topicString.Split('.');
            var channelId = int.Parse(topicParts[^1]);
            var twitchMessage = data.GetProperty("message")!.GetString()!;

            if (twitchMessage.Contains("\"type\":\"stream-up\""))
            {
                OnStreamStateUpdated?.Invoke(this, channelId, true);
                return;
            }

            if (twitchMessage.Contains("\"type\":\"stream-down\""))
            {
                OnStreamStateUpdated?.Invoke(this, channelId, false);
                return;
            }
            _logger.Info("Message from Twitch was unhandled");
            _logger.Info(message.Text);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Twitch");
            _logger.Error(e);
            _logger.Error("--- JSON Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of JSON Payload ---");
        }
    }

    public async Task<bool> IsStreamLive(string channel)
    {
        var clientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        var graphQl = "query {\n  user(login: \"" + channel + "\") {\n    stream {\n      id\n    }\n  }\n}";
        _logger.Debug($"Built GraphQL query string: {graphQl}");
        var jsonBody = new Dictionary<string, object>
        {
            { "query", graphQl },
            { "variables", new object() }
        };
        _logger.Debug("Created dictionary object for the JSON payload, should serialize to following value:");
        _logger.Debug(JsonSerializer.Serialize(jsonBody));
        var handler = new HttpClientHandler {AutomaticDecompression = DecompressionMethods.All};
        if (_proxy != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(_proxy);
            _logger.Debug($"Configured to use proxy {_proxy}");
        }

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("client-id", clientId);
        var postBody = JsonContent.Create(jsonBody);
        var response = await client.PostAsync("https://gql.twitch.tv/gql", postBody, _cancellationToken);
        //response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Twitch API returned following JSON");
        _logger.Debug(responseContent.GetRawText);
        if (responseContent.GetProperty("data").GetProperty("user").ValueKind == JsonValueKind.Null)
        {
            _logger.Debug("data.user was null");
            throw new TwitchUserNotFoundException();
        }

        if (responseContent.GetProperty("data").GetProperty("user").GetProperty("stream").ValueKind ==
            JsonValueKind.Null)
        {
            _logger.Debug("stream property was null. Means streamer is not live");
            return false;
        }

        return true;
    }

    public class TwitchUserNotFoundException : Exception;
}