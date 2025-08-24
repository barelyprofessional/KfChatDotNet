using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class Owncast(ChatBot kfChatBot) : IDisposable
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
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.OwncastCheckInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_liveStatusCheckTaskCts.Token))
        {
            var ct = _liveStatusCheckTaskCts.Token;
            _logger.Debug("Going to check if anyone is live on Owncast now");
            var persistedLive = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.OwncastPersistedCurrentlyLive))
                .ToBoolean();
            OwncastStatusModel status;
            try
            {
                status = await GetLiveStatus(ct);
            }
            catch (Exception e)
            {
                _logger.Error("Caught an error while trying to get the currently live streams from Owncast");
                _logger.Error(e);
                continue;
            }
            if (status.Online == persistedLive) continue;
            await SettingsProvider.SetValueAsBooleanAsync(BuiltIn.Keys.OwncastPersistedCurrentlyLive,
                status.Online);
            if (!status.Online) continue;
            await kfChatBot.SendChatMessageAsync("https://bossmanjack.tv restream is live!", true);
            if (!(await SettingsProvider.GetValueAsync(BuiltIn.Keys.CaptureEnabled)).ToBoolean()) continue;
            _ = new StreamCapture("https://bossmanjack.tv", StreamCaptureMethods.YtDlp, ct);
        }
    }
    
    public static async Task<OwncastStatusModel> GetLiveStatus(CancellationToken ct = default)
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
            logger.Debug($"Set proxy for the Owncast API request to {proxy.Value}");
        }
        
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await client.GetAsync("https://bossmanjack.tv/api/status", ct);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        logger.Debug("Owncast endpoint returned the following JSON");
        logger.Debug(content.GetRawText);
        return content.Deserialize<OwncastStatusModel>() ?? throw new InvalidOperationException();
    }

    public void Dispose()
    {
        _liveStatusCheckTaskCts.Cancel();
        _liveStatusCheckTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class OwncastStatusModel
{
    [JsonPropertyName("online")]
    public required bool Online { get; set; }
}