using System.Net;
using System.Net.Http.Json;
using FlareSolverrSharp;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class Rainbet : IDisposable
{
    private Logger _logger = LogManager.GetCurrentClassLogger();
    public delegate void OnRainbetBetEventHandler(object sender, List<RainbetBetHistoryModel> bets);
    public event OnRainbetBetEventHandler OnRainbetBet;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private CancellationTokenSource _gameHistoryCts = new();
    private Task? _gameHistoryTask;
    private TimeSpan _gameHistoryInterval = TimeSpan.FromSeconds(60);
    private IEnumerable<string>? _cookies = null;
    private string? _userAgent = null;
    public DateTimeOffset LastSuccessfulRefresh = DateTimeOffset.MinValue;

    public Rainbet(CancellationToken? cancellationToken = null)
    {
        if (cancellationToken != null) _cancellationToken = cancellationToken.Value;
        _logger.Info("Rainbet client created");
    }

    public void StartGameHistoryTimer()
    {
        _gameHistoryTask = GameHistoryTimer();
    }
    
    private async Task GameHistoryTimer()
    {
        using var timer = new PeriodicTimer(_gameHistoryInterval);
        while (await timer.WaitForNextTickAsync(_gameHistoryCts.Token))
        {
            try
            {
                _logger.Info($"Retrieving game history from Rainbet, last success: {LastSuccessfulRefresh:yyyy-MM-dd HH:mm:ss}");
                var bets = await GetGameHistory(5000);
                OnRainbetBet?.Invoke(this, bets);
                LastSuccessfulRefresh = DateTimeOffset.UtcNow;
            }
            catch (Exception e)
            {
                _logger.Error("Caught error when retrieving bets and invoking the event");
                _logger.Error(e);
            }
        }
    }

    private async Task RefreshCookies()
    {
        var settings =
            await Helpers.GetMultipleValues([BuiltIn.Keys.FlareSolverrApiUrl, BuiltIn.Keys.FlareSolverrProxy]);
        var flareSolverrUrl = settings[BuiltIn.Keys.FlareSolverrApiUrl];
        var flareSolverrProxy = settings[BuiltIn.Keys.FlareSolverrProxy];
        var handler = new ClearanceHandler(flareSolverrUrl.Value)
        {
            // Generally takes <5 seconds
            MaxTimeout = 30000,
        };
        _logger.Debug($"Configured clearance handler to use FlareSolverr endpoint: {flareSolverrUrl.Value}");
        // I would suggest not using a proxy. It's pretty much a miracle this works at all.
        if (flareSolverrProxy.Value != null)
        {
            handler.ProxyUrl = flareSolverrProxy.Value;
            _logger.Debug($"Configured clearance handler to use {flareSolverrProxy.Value} for proxying the request");
        }
        var client = new HttpClient(handler);
        // You get CF checkbox'd if you go directly to sportsbook.rainbet.com but works ok for root
        var getResponse = await client.GetAsync("https://rainbet.com/", _cancellationToken);
        _cookies = getResponse.Headers.GetValues("Set-Cookie");
        _userAgent = getResponse.RequestMessage!.Headers.UserAgent.ToString();
    }

    // FlareSolverr C# client does not support POSTing application/json so this method involves
    // 1. Getting the home page (as you're unlikely to get a CF challenge checkbox. Probably due to some config to limit
    // friction for degens on VPNs, which is like 99% of the traffic for these shit casinos)
    // 2. Using the cookies from that request to do the actual POST
    // Cookies and UA must match or the trannies at Cloudflare will reject your cookies
    // take = 10 is the default, but it can go higher
    public async Task<List<RainbetBetHistoryModel>> GetGameHistory(int take = 10)
    {
        if (_cookies == null)
        {
            _logger.Info("Retrieving cookies for Rainbet as they have not been retrieved yet");
            await RefreshCookies();
        }
        var flareSolverrProxy = await Helpers.GetValue(BuiltIn.Keys.FlareSolverrProxy);
        var gameHistoryUrl = "https://sportsbook.rainbet.com/v1/game-history";
        var jsonBody = new Dictionary<string, int> { {"take", take} };
        var postData = JsonContent.Create(jsonBody);
        var postClientHandler = new HttpClientHandler();
        if (flareSolverrProxy.Value != null)
        {
            postClientHandler.Proxy = new WebProxy(flareSolverrProxy.Value);
            postClientHandler.UseProxy = true;
            _logger.Debug($"Configured API request to use {flareSolverrProxy.Value}");
        }
        var postClient = new HttpClient(postClientHandler);
        postClient.DefaultRequestHeaders.Add("Cookie", _cookies!);
        postClient.DefaultRequestHeaders.UserAgent.Clear();
        postClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        var response = await postClient.PostAsync(gameHistoryUrl, postData, _cancellationToken);
        if (response.Headers.Contains("cf-mitigated"))
        {
            _logger.Error("Hit a Cloudflare challenge, cookies expired? Marking cookies as null so it'll refresh next time we run");
            throw new Exception("Cloudflare challenged");
        }
        var bets = await response.Content.ReadFromJsonAsync<List<RainbetBetHistoryModel>>(cancellationToken: _cancellationToken);
        return bets;
    }

    public void Dispose()
    {
        _gameHistoryCts.Cancel();
        _gameHistoryCts.Dispose();
        _gameHistoryTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}