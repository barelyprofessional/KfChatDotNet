using System.ComponentModel;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class DiscordService : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient? _wsClient;
    private readonly Uri _wsUri = new Uri("wss://gateway.discord.gg/?encoding=json&v=9");
    // Not sure what a good value for this would be
    private const int ReconnectTimeout = 60;
    private readonly string? _proxy;
    private readonly string _authorization;
    private int _sequence;
    private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/115.0";

    public delegate void MessageReceivedEventHandler(object sender, DiscordMessageModel message);
    public delegate void PresenceUpdateEventHandler(object sender, DiscordPresenceUpdateModel presence);
    public delegate void WsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public delegate void InvalidCredentialsEventHandler(object sender, DiscordPacketReadModel packet);
    public delegate void ChannelCreatedEventHandler(object sender, DiscordChannelCreationModel channel);
    public delegate void ChannelDeletedEventHandler(object sender, DiscordChannelDeletionModel channel);
    public delegate void ConversationSummaryUpdateEventHandler(object sender,
        DiscordConversationSummaryUpdateModel summary, string guildId);

    public event MessageReceivedEventHandler? OnMessageReceived;
    public event PresenceUpdateEventHandler? OnPresenceUpdated;
    public event WsDisconnectionEventHandler? OnWsDisconnection;
    public event InvalidCredentialsEventHandler? OnInvalidCredentials;
    public event ChannelCreatedEventHandler? OnChannelCreated;
    public event ChannelDeletedEventHandler? OnChannelDeleted;
    public event ConversationSummaryUpdateEventHandler? OnConversationSummaryUpdate;

    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _pingCts = new();
    private Task? _heartbeatTask;
    // Discord tells us the heartbeat interval to use in the op 10 response so this is just a placeholder
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(40);

    public DiscordService(string authorization, string? proxy = null, CancellationToken cancellationToken = default)
    {
        _proxy = proxy;
        _cancellationToken = cancellationToken;
        _authorization = authorization;
        _logger.Info("Discord Service created");
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
            clientWs.Options.SetRequestHeader("Origin", "https://discord.com");
            clientWs.Options.SetRequestHeader("User-Agent", UserAgent);
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(ReconnectTimeout),
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
            var heartbeatPacket = JsonSerializer.Serialize(new DiscordPacketWriteModel
            {
                OpCode = 1,
                Sequence = _sequence,
            });
            _logger.Debug("Sending heartbeat packet");
            _logger.Debug(heartbeatPacket);
            await _wsClient.SendInstant(heartbeatPacket);
        }
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Discord (or never successfully connected). Type is {disconnectionInfo.Type}");
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
            _logger.Info("Discord sent a null message");
            return;
        }

        _logger.Debug($"Received event from Discord: {message.Text}");

        try
        {
            var packet = JsonSerializer.Deserialize<DiscordPacketReadModel>(message.Text);
            if (packet == null) throw new InvalidOperationException("Caught a null when deserializing Discord packet");
            _sequence = packet.Sequence ?? _sequence;
            if (packet.OpCode == 9)
            {
                _logger.Info("Discord sent op code indicating invalid credentials. Raising event.");
                OnInvalidCredentials?.Invoke(this, packet);
                return;
            }

            if (packet.OpCode == 10)
            {
                _logger.Info("Discord op code 10 (hello) received. Setting up heartbeat timer and sending init");
                _logger.Info("Sending connection_init");
                var initPayload =
                    "{\"op\":2,\"d\":{\"token\":\"" + _authorization + "\",\"capabilities\":30717,\"properties\":" +
                    "{\"os\":\"Linux\",\"browser\":\"Firefox\",\"device\":\"\",\"system_locale\":\"en-US\",\"browser_user_agent\":" +
                    "\"" + UserAgent + "\",\"browser_version\":\"115.0\",\"os_version\":\"\",\"referrer\":\"\",\"referring_domain\":\"\"," +
                    "\"referrer_current\":\"\",\"referring_domain_current\":\"\",\"release_channel\":\"stable\"," +
                    "\"client_build_number\":306208,\"client_event_source\":null,\"design_id\":0},\"presence\":{\"status\":\"unknown\"," +
                    "\"since\":0,\"activities\":[],\"afk\":false},\"compress\":false,\"client_state\":{\"guild_versions\":{}}}}";
                _logger.Debug(initPayload);
                _wsClient?.SendInstant(initPayload).Wait(_cancellationToken);
                _heartbeatInterval =
                    TimeSpan.FromMilliseconds(packet.Data.GetProperty("heartbeat_interval").GetInt32());
                if (_heartbeatTask != null) return;
                _heartbeatTask = Task.Run(HeartbeatTimer, _cancellationToken);
                return;
            }

            if (packet.OpCode == 11)
            {
                _logger.Info("Received heartbeat ack from Discord");
                return;
            }

            if (packet.OpCode != 0)
            {
                _logger.Info($"Op code {packet.OpCode} was unhandled. JSON follows");
                _logger.Info(message.Text);
                return;
            }

            switch (packet.DispatchEvent)
            {
                case null:
                    throw new InvalidOperationException("t was null");
                case "READY":
                    _logger.Info("Discord is now ready");
                    return;
                case "PRESENCE_UPDATE":
                    OnPresenceUpdated?.Invoke(this,
                        packet.Data.Deserialize<DiscordPresenceUpdateModel>() ?? throw new InvalidOperationException());
                    return;
                case "MESSAGE_CREATE":
                    OnMessageReceived?.Invoke(this,
                        packet.Data.Deserialize<DiscordMessageModel>() ?? throw new InvalidOperationException());
                    return;
                case "CHANNEL_CREATE":
                    OnChannelCreated?.Invoke(this,
                        packet.Data.Deserialize<DiscordChannelCreationModel>() ??
                        throw new InvalidOperationException());
                    return;
                case "CHANNEL_DELETE":
                    OnChannelDeleted?.Invoke(this,
                        packet.Data.Deserialize<DiscordChannelDeletionModel>() ??
                        throw new InvalidOperationException());
                    return;
                case "CONVERSATION_SUMMARY_UPDATE":
                    var guildId = packet.Data.GetProperty("guild_id").GetString();
                    var summaries = packet.Data.GetProperty("summaries")
                        .Deserialize<List<DiscordConversationSummaryUpdateModel>>();
                    if (summaries == null) return;
                    if (summaries.Count == 0) return;
                    foreach (var summary in summaries)
                    {
                        OnConversationSummaryUpdate?.Invoke(this, summary, guildId ?? string.Empty);
                    }
                    return;
                default:
                    _logger.Debug($"{packet.DispatchEvent} was unhandled. JSON follows");
                    _logger.Debug(message.Text);
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Discord");
            _logger.Error(e);
            _logger.Error("--- JSON Payload ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of JSON Payload ---");
        }
    }

    public void Dispose()
    {
        _logger.Info("Disposing of the Discord service");
        _wsClient?.Dispose();
        _pingCts.Cancel();
        _pingCts.Dispose();
        _heartbeatTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class DiscordPacketModel<T>
{
    [JsonPropertyName("t")]
    public string? DispatchEvent { get; set; }
    [JsonPropertyName("op")]
    public int OpCode { get; set; }
    [JsonPropertyName("d")]
    public T? Data { get; set; }
    [JsonPropertyName("s")]
    public int? Sequence { get; set; }
}

public class DiscordPacketWriteModel : DiscordPacketModel<object>;
public class DiscordPacketReadModel : DiscordPacketModel<JsonElement>;

public class DiscordPresenceUpdateModel
{
    [JsonPropertyName("user")]
    public required DiscordUserModel User { get; set; }
    [JsonPropertyName("status")]
    public required string Status { get; set; }
    [JsonPropertyName("client_status")]
    public required Dictionary<string, string> ClientStatus { get; set; }
}

public class DiscordUserModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }
}

public class DiscordMessageModel
{
    [JsonPropertyName("type")]
    public required DiscordMessageType Type { get; set; }
    [JsonPropertyName("content")]
    public string? Content { get; set; }
    [JsonPropertyName("author")]
    public required DiscordUserModel Author { get; set; }
    [JsonPropertyName("attachments")]
    public JsonElement[]? Attachments { get; set; }
}

public class DiscordChannelCreationModel
{
    [JsonPropertyName("type")]
    public required DiscordChannelType Type { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("guild_id")]
    public required string GuildId { get; set; }
}

public class DiscordChannelDeletionModel
{
    [JsonPropertyName("type")]
    public required DiscordChannelType Type { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class DiscordConversationSummaryUpdateModel
{
    [JsonPropertyName("unsafe")]
    public required bool Unsafe { get; set; }
    [JsonPropertyName("topic")]
    public required string Topic { get; set; }
    [JsonPropertyName("summ_short")]
    public required string SummaryShort { get; set; }
    /// <summary>
    /// List of Discord IDs for people whose messages were used to generate the summary
    /// </summary>
    [JsonPropertyName("people")]
    public required List<string> People { get; set; }
}

// https://discord.com/developers/docs/resources/channel#channel-object-channel-types
// Ignored the ones nobody cares about
public enum DiscordChannelType
{
    [Description("Text")]
    GuildText = 0,
    [Description("Voice")]
    GuildVoice = 2,
    [Description("Stage")]
    GuildStageVoice = 13
}

public enum DiscordMessageType
{
    Default = 0,
    [Description("Stage start")]
    StageStart = 27,
    [Description("Stage end")]
    StageEnd = 28
}