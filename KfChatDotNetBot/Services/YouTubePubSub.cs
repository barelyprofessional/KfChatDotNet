using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;

namespace KfChatDotNetBot.Services;

public class YouTubePubSub : IDisposable
{
    private string _connectionString;
    private CancellationToken _ct;
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private ConnectionMultiplexer? _redis;
    private ISubscriber? _sub;

    public delegate void OnNewVideoEventHandler(object sender, YouTubePubSubNotificationModel data);

    public event OnNewVideoEventHandler? OnNewVideo;
    
    public YouTubePubSub(CancellationToken cancellationToken = default)
    {
        _ct = cancellationToken;
        _connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.YouTubePubSubRedisConnectionString).Result
            .Value ?? throw new InvalidOperationException("YouTube PubSub Redis connection string was not defined");
    }

    public async Task Connect()
    {
        var channel = await SettingsProvider.GetValueAsync(BuiltIn.Keys.YouTubePubSubRedisChannel);
        if (channel.Value == null)
        {
            throw new InvalidOperationException("Redis channel was null");
        }
        _redis = await ConnectionMultiplexer.ConnectAsync(_connectionString);
        _sub = _redis.GetSubscriber();
        await _sub.SubscribeAsync(new RedisChannel(channel.Value, RedisChannel.PatternMode.Literal), PubSubMessageReceived);
    }

    public bool IsConnected()
    {
        if (_redis == null || _sub == null) return false;
        return _sub.IsConnected();
    }

    private void PubSubMessageReceived(RedisChannel channel, RedisValue message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<YouTubePubSubNotificationModel>(message.ToString());
            if (payload == null)
            {
                throw new InvalidOperationException("Caught a null when attempting to deserialize the PubSub JSON");
            }
            OnNewVideo?.Invoke(this, payload);
        }
        catch (Exception e)
        {
            _logger.Error("PubSub shit itself when trying to handle a message");
            _logger.Error(e);
            _logger.Error("--- Payload ---");
            _logger.Error(message.ToString());
        }
    }

    public void Dispose()
    {
        _sub?.UnsubscribeAll();
        _redis?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class YouTubePubSubNotificationModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("title")]
    public required string Title { get; set; }
    [JsonPropertyName("url")]
    public required string Url { get; set; }
    [JsonPropertyName("channel")]
    public required YouTubePubSubNotificationChannelModel Channel { get; set; }
}

public class YouTubePubSubNotificationChannelModel
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}