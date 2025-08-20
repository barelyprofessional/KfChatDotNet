using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class TwitchGraphQl(string? proxy = null, CancellationToken cancellationToken = default) : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private Uri _gqlEndpoint = new Uri("https://gql.twitch.tv/gql");
    private string _gqlClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private string? _proxy = proxy;
    private CancellationToken _cancellationToken = cancellationToken;
    private Task? _liveStatusCheckTask;
    private CancellationTokenSource _liveStatusCheckTaskCts = new();
    
    public delegate void OnStreamStateUpdateEventHandler(object sender, string channelName, bool isLive);
    public event OnStreamStateUpdateEventHandler? OnStreamStateUpdated;
    
    public void StartLiveStatusCheck()
    {
        _liveStatusCheckTaskCts = new CancellationTokenSource();
        _liveStatusCheckTask = Task.Run(LiveStatusCheckTask, _liveStatusCheckTaskCts.Token);
    }

    public bool IsTaskRunning()
    {
        if (_liveStatusCheckTask?.Status is not (TaskStatus.Running or TaskStatus.WaitingForActivation)) return false;
        return !_liveStatusCheckTaskCts.IsCancellationRequested;
    }

    private async Task LiveStatusCheckTask()
    {
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.TwitchGraphQlCheckInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_liveStatusCheckTaskCts.Token))
        {
            var ct = _liveStatusCheckTaskCts.Token;
            _logger.Debug("Going to check if Bossman is live right now");
            var settings = await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive
            ]);
            TwitchGraphQlModel stream;
            try
            {
                stream = await GetStream(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!);
            }
            catch (Exception e)
            {
                _logger.Error("Caught exception when trying to check if Bossman is live on Twitch");
                _logger.Error(e);
                continue;
            }

            if (stream.IsLive)
            {
                _logger.Info("Updating DB with fresh view count");
                await using var db = new ApplicationDbContext();
                await db.TwitchViewCounts.AddAsync(new TwitchViewCountDbModel
                {
                    Topic = stream.Id!,
                    Viewers = stream.ViewerCount!.Value,
                    Time = DateTimeOffset.UtcNow,
                    ServerTime = 0
                }, ct);
                await db.SaveChangesAsync(ct);
            }

            var persistedLive = settings[BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive].ToBoolean();
            if (stream.IsLive == persistedLive) continue;
            OnStreamStateUpdated?.Invoke(this, settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!, stream.IsLive);
            await SettingsProvider.SetValueAsBooleanAsync(BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive,
                stream.IsLive);
        }
    }

    public async Task<TwitchGraphQlModel> GetStream(string channel)
    {
        var graphQl = "query {\n  user(login: \"" + channel + "\") {\n    stream {\n      id\n      viewersCount\n    }\n  }\n}";
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
        client.DefaultRequestHeaders.Add("client-id", _gqlClientId);
        var postBody = JsonContent.Create(jsonBody);
        var response = await client.PostAsync(_gqlEndpoint, postBody, _cancellationToken);
        var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: _cancellationToken);
        _logger.Debug("Twitch API returned following JSON");
        _logger.Debug(responseContent.GetRawText);
        var returnData = new TwitchGraphQlModel { IsLive = false};
        if (responseContent.GetProperty("data").GetProperty("user").ValueKind == JsonValueKind.Null)
        {
            _logger.Error("data.user was null");
            return returnData;
        }

        if (responseContent.GetProperty("data").GetProperty("user").GetProperty("stream").ValueKind ==
            JsonValueKind.Null)
        {
            _logger.Debug("stream property was null. Means streamer is not live");
            return returnData;
        }

        returnData.IsLive = true;
        returnData.Id = responseContent.GetProperty("data").GetProperty("user").GetProperty("stream").GetProperty("id")
            .GetString();
        returnData.ViewerCount = responseContent.GetProperty("data").GetProperty("user").GetProperty("stream").GetProperty("viewersCount")
            .GetInt32();
        return returnData;
    }
    
    public void Dispose()
    {
        _liveStatusCheckTaskCts.Cancel();
        _liveStatusCheckTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class TwitchGraphQlModel
{
    /// <summary>
    /// Whether the streamer is live
    /// </summary>
    public required bool IsLive { get; set; }
    /// <summary>
    /// Viewer count, null if not live
    /// </summary>
    public int? ViewerCount { get; set; }
    /// <summary>
    /// Stream ID returned by the GraphQL endpoint. null if none
    /// </summary>
    public string? Id { get; set; }
}