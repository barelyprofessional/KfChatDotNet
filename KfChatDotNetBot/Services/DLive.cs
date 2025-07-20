using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot.Services;

public class DLive(ChatBot kfChatBot) : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Task? _liveStatusCheckTask;
    private CancellationTokenSource _liveStatusCheckTaskCts = new();

    public void StartLiveStatusCheck()
    {
        _liveStatusCheckTaskCts = new CancellationTokenSource();
        _liveStatusCheckTask = Task.Run(LiveStatusCheckTask, _liveStatusCheckTaskCts.Token);
    }
    
    private async Task LiveStatusCheckTask()
    {
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.DLiveCheckInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_liveStatusCheckTaskCts.Token))
        {
            var ct = _liveStatusCheckTaskCts.Token;
            _logger.Debug("Going to check if anyone is live on DLive now");
            await using var db = new ApplicationDbContext();
            var streams = await db.Streams.Where(s => s.Service == StreamService.DLive).Include(s => s.User).ToListAsync(ct);
            var settings = await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.DLivePersistedCurrentlyLiveStreams, BuiltIn.Keys.CaptureEnabled
            ]);
            var currentlyLive = settings[BuiltIn.Keys.DLivePersistedCurrentlyLiveStreams].JsonDeserialize<List<string>>() ?? [];
            foreach (var stream in streams)
            {
                var username = stream.StreamUrl.Split('/').LastOrDefault();
                if (username == null)
                {
                    _logger.Error($"Could not determine the DLive username from {stream.StreamUrl} in row {stream.Id}");
                    continue;
                }

                var status = await IsLive(username, ct);
                if (!status.IsLive)
                {
                    currentlyLive.Remove(username);
                    continue;
                }
                // Already known to be live so do nothing
                if (currentlyLive.Contains(username)) continue;
                
                var identity = "A streamer";
                if (stream.User != null)
                {
                    identity = "@" + stream.User.KfUsername;
                }

                await kfChatBot.SendChatMessageAsync($"{identity} is live! {status.Title} {stream.StreamUrl}", true);

                if (stream.AutoCapture && settings[BuiltIn.Keys.CaptureEnabled].ToBoolean())
                {
                    _logger.Info($"{stream.StreamUrl} is live and set to auto capture");
                    _ = new StreamCapture(stream.StreamUrl, StreamCaptureMethods.Streamlink, ct).CaptureAsync();
                }
                currentlyLive.Add(username);
            }
            
            _logger.Debug($"Persisting currently live streams, count is {currentlyLive.Count}");
            await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.DLivePersistedCurrentlyLiveStreams,
                currentlyLive);
        }
    }
    
    public static async Task<DLiveIsLiveModel> IsLive(string username, CancellationToken ct = default)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var proxy = await SettingsProvider.GetValueAsync(BuiltIn.Keys.Proxy);
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,

        };
        if (proxy.Value != null)
        {
            handler.Proxy = new WebProxy(proxy.Value);
            handler.UseProxy = true;
            logger.Debug($"Set proxy for the DLive GraphQL request to {proxy.Value}");
        }

        var gql = "query { userByDisplayName(displayname:\"" + username + "\") { livestream " +
                  "{ content createdAt title thumbnailUrl watchingCount } username } }";
        logger.Debug($"Built GraphQL query string: {gql}");
        var jsonBody = new Dictionary<string, object>
        {
            { "query", gql }
        };
        logger.Debug("Created dictionary object for the JSON payload, should serialize to following:");
        logger.Debug(JsonSerializer.Serialize(jsonBody));
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var postBody = JsonContent.Create(jsonBody);
        var response = await client.PostAsync("https://graphigo.prd.dlive.tv/", postBody, ct);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        logger.Debug("DLive GraphQL endpoint returned the following JSON");
        logger.Debug(content.GetRawText);
        // Not live
        // {"data":{"userByDisplayName":{"livestream":null,"username":"planesfan"}}}
        // Live
        // {
        //     "data": {
        //         "userByDisplayName": {
        //             "livestream": {
        //                 "content": "",
        //                 "createdAt": "1752699050000",
        //                 "title": "HUNT FULL ACHAT VENEZ DONNER VOS CALL!!!",
        //                 "thumbnailUrl": "https://images.prd.dlivecdn.com/live-thumbnail/a587c2a8-6288-11f0-90fc-d638708e4bb8",
        //                 "watchingCount": 799
        //             },
        //             "username": "cashpistache1"
        //         }
        //     }
        // }
        var responseData = content.GetProperty("data").GetProperty("userByDisplayName");
        var isLive = responseData.GetProperty("livestream").ValueKind == JsonValueKind.Object;
        string? title = null;
        if (isLive)
        {
            title = responseData.GetProperty("livestream").GetProperty("title").GetString();
        }
        return new DLiveIsLiveModel
        {
            IsLive = isLive,
            Title = title,
            Username = responseData.GetProperty("username").GetString() ?? "username was null in GraphQL response"
        };
    }

    public void Dispose()
    {
        _liveStatusCheckTaskCts.Cancel();
        _liveStatusCheckTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}