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

public class PeerTube(ChatBot kfChatBot) : IDisposable
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
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiPeerTubeCheckInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_liveStatusCheckTaskCts.Token))
        {
            var ct = _liveStatusCheckTaskCts.Token;
            _logger.Debug("Going to check if anyone is live on PeerTube now");
            await using var db = new ApplicationDbContext();
            var streams = await db.Streams.Where(s => s.Service == StreamService.KiwiPeerTube).Include(s => s.User).ToListAsync(ct);
            var settings = await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiPeerTubePersistedCurrentlyLiveStreams, BuiltIn.Keys.CaptureEnabled, BuiltIn.Keys.KiwiPeerTubeEnabled,
                BuiltIn.Keys.KiwiPeerTubeEnforceWhitelist
            ]);
            if (!settings[BuiltIn.Keys.KiwiPeerTubeEnabled].ToBoolean())
            {
                _logger.Debug("PeerTube disabled");
                continue;
            }
            var persistedLive = settings[BuiltIn.Keys.KiwiPeerTubePersistedCurrentlyLiveStreams].JsonDeserialize<List<string>>() ?? [];
            List<PeerTubeVideoDataModel> currentlyLive;
            try
            {
                currentlyLive = await GetLiveStreams(ct);
            }
            catch (Exception e)
            {
                _logger.Error("Caught an error while trying to get the currently live streams from Kiwi PeerTube");
                _logger.Error(e);
                continue;
            }
            foreach (var stream in currentlyLive)
            {
                if (persistedLive.Contains(stream.Uuid)) continue;
                StreamDbModel? dbEntry = null;
                PeerTubeMetaModel? meta = null;
                foreach (var row in streams)
                {
                    if (row.Metadata == null)
                    {
                        _logger.Error($"Stream ID {row.Id} has null metadata");
                        continue;
                    }
                    meta = JsonSerializer.Deserialize<PeerTubeMetaModel>(row.Metadata);
                    if (meta == null)
                    {
                        _logger.Error($"Caught a null when deserializing the metadata for {row.Id}");
                        continue;
                    }

                    if (meta.AccountName != stream.Account.Name) continue;
                    dbEntry = row;
                    break;
                }
                if (settings[BuiltIn.Keys.KiwiPeerTubeEnforceWhitelist].ToBoolean() && dbEntry == null)
                {
                    _logger.Info($"{stream.Account.Name} is live but whitelisting is enforced and the username isn't in the stream database");
                    continue;
                }
                
                persistedLive.Add(stream.Uuid);
                await kfChatBot.SendChatMessageAsync($"@{stream.Account.DisplayName} is live! {stream.Name} {stream.Url}", true);

                if (settings[BuiltIn.Keys.CaptureEnabled].ToBoolean())
                {
                    if (dbEntry != null && !dbEntry.AutoCapture)
                    {
                        _logger.Info($"{stream.Url} is live but auto capture is disabled for this stream");
                        continue;
                    }
                    _logger.Info($"{stream.Url} is live and set to auto capture (if configured)");
                    _ = new StreamCapture(stream.Url, StreamCaptureMethods.YtDlp, meta?.CaptureOverrides, ct).CaptureAsync();
                }
            }

            // The ToList will create a copy of the list so we can work on the original one
            foreach (var persisted in persistedLive.ToList())
            {
                var stream = currentlyLive.FirstOrDefault(l => l.Uuid == persisted);
                if (stream == null) persistedLive.Remove(persisted);
            }
            
            _logger.Debug($"Persisting currently live streams, count is {currentlyLive.Count}");
            await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KiwiPeerTubePersistedCurrentlyLiveStreams,
                persistedLive);
        }
    }
    
    public static async Task<List<PeerTubeVideoDataModel>> GetLiveStreams(CancellationToken ct = default)
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
            logger.Debug($"Set proxy for the PeerTube API request to {proxy.Value}");
        }
        
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await client.GetAsync("https://kiwifarms.tv/api/v1/videos?start=0&count=25&sort=-publishedAt&skipCount=true&nsfw=both&nsfwFlagsExcluded=0&nsfwFlagsIncluded=0&isLive=true", ct);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        logger.Debug("PeerTube endpoint returned the following JSON");
        logger.Debug(content.GetRawText);
        return content.GetProperty("data").Deserialize<List<PeerTubeVideoDataModel>>() ?? throw new InvalidOperationException();
    }

    public void Dispose()
    {
        _liveStatusCheckTaskCts.Cancel();
        _liveStatusCheckTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}