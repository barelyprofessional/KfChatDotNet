using System.Text.Json;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KickWsClient.Models;
using Microsoft.EntityFrameworkCore;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

/// <summary>
/// The glue that binds the chatbot to all the third-party services beyond Sneedchat
/// Mostly just trying to move code out of ChatBot.cs as it's ridiculously large now
/// </summary>
public class BotServices
{
    private readonly ChatBot _chatBot;
    private readonly CancellationToken _cancellationToken;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    internal KickWsClient.KickWsClient? KickClient;
    private TwitchGraphQl? _twitch;
    private Shuffle? _shuffle;
    private DiscordService? _discord;
    private TwitchChat? _twitchChat;
    private Jackpot? _jackpot;
    private Howlgg? _howlgg;
    private RainbetWs? _rainbet;
    private Chipsgg? _chipsgg;
    private Clashgg? _clashgg;
    private BetBolt? _betBolt;
    private Yeet? _yeet;
    public AlmanacShill? AlmanacShill;
    private Parti? _parti;
    private DLive? _dliveStatusCheck;
    private PeerTube? _peerTubeStatusCheck;
    private Owncast? _owncastStatusCheck;
    private ShuffleDotUs? _shuffleDotUs;
    private YouTubePubSub? _youTubePubSub;
    public KasinoRain? KasinoRain;
    
    private Task? _websocketWatchdog;
    private Task? _howlggGetUserTimer;
    
    private string? _bmjTwitchUsername;
    private bool _twitchDisabled;
    private Dictionary<string, SeenYeetBet> _yeetBets = new();
    
    // lol
    internal bool TemporarilyBypassGambaSeshForDiscord;
    internal bool TemporarilySuppressGambaMessages = false;
    internal bool TemporarilyForceGambaMessages = false;

    public BotServices(ChatBot botInstance, CancellationToken ctx)
    {
        _chatBot = botInstance;
        _cancellationToken = ctx;
        TemporarilyBypassGambaSeshForDiscord =
            SettingsProvider.GetValueAsync(BuiltIn.Keys.DiscordTemporarilyBypassGambaSeshInitialValue).Result.ToBoolean();
        
        _logger.Info("Bot services ready to initialize!");
    }

    public void InitializeServices()
    {
        if (_websocketWatchdog != null)
        {
            _logger.Error("InitializeServices method was called but we initialized as _websocketWatchdog is not null?");
            throw new InvalidOperationException("Bot services already initialized");
        }
        _logger.Info("Initializing services");
        Task[] tasks =
        [
            BuildShuffle(),
            BuildDiscord(),
            BuildTwitchChat(),
            BuildHowlgg(),
            BuildJackpot(),
            BuildChipsgg(),
            BuildKick(),
            BuildTwitch(),
            BuildClashgg(),
            BuildAlmanacShill(),
            BuildBetBolt(),
            BuildYeet(),
            BuildRainbet(),
            BuildParti(),
            BuildDLiveStatusCheck(),
            BuildPeerTubeLiveStatusCheck(),
            BuildOwncastLiveStatusCheck(),
            BuildShuffleDotUs(),
            BuildYouTubePubSub(),
            BuildKasinoRain()
        ];
        try
        {
            Task.WaitAll(tasks, _cancellationToken);
        }
        catch (Exception e)
        {
            _logger.Error("A service failed, exception follows");
            _logger.Error(e);
        }
        
        _logger.Info("Starting websocket watchdog and Howl.gg user stats timer");
        _websocketWatchdog = WebsocketWatchdog();
        _howlggGetUserTimer = HowlggGetUserTimer();
    }

    private async Task BuildKasinoRain()
    {
        _logger.Debug("Building the Kasino Rain thingy");
        KasinoRain = new KasinoRain(_chatBot, _cancellationToken);
    }
    
    private async Task BuildShuffle()
    {
        _logger.Debug("Building Shuffle");
        _shuffle = new Shuffle((await SettingsProvider.GetValueAsync(BuiltIn.Keys.Proxy)).Value, _cancellationToken);
        _shuffle.OnLatestBetUpdated += ShuffleOnLatestBetUpdated;
        await _shuffle.StartWsClient();
    }
    
    private async Task BuildShuffleDotUs()
    {
        _logger.Debug("Building Shuffle.us");
        _shuffleDotUs = new ShuffleDotUs((await SettingsProvider.GetValueAsync(BuiltIn.Keys.Proxy)).Value, _cancellationToken);
        _shuffleDotUs.OnLatestBetUpdated += ShuffleOnLatestBetUpdated;
        await _shuffleDotUs.StartWsClient();
    }
    
    private async Task BuildYouTubePubSub()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.YouTubePubSubEnabled]);
        if (!settings[BuiltIn.Keys.YouTubePubSubEnabled].ToBoolean())
        {
            _logger.Debug("YouTube PubSub is disabled");
            return;
        }
        _youTubePubSub = new YouTubePubSub(_cancellationToken);
        _youTubePubSub.OnNewVideo += YouTubePubSubOnNewVideo;
        await _youTubePubSub.Connect();
        _logger.Info("Built YouTube PubSub");
    }

    private async Task BuildDiscord()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.DiscordToken, BuiltIn.Keys.Proxy]);
        _logger.Debug("Building Discord");
        if (settings[BuiltIn.Keys.DiscordToken].Value == null)
        {
            _logger.Info("Not building Discord as the token is not configured");
            return;
        }
        _discord = new DiscordService(settings[BuiltIn.Keys.DiscordToken].Value!, settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _discord.OnInvalidCredentials += DiscordOnInvalidCredentials;
        _discord.OnMessageReceived += DiscordOnMessageReceived;
        _discord.OnPresenceUpdated += DiscordOnPresenceUpdated;
        _discord.OnChannelCreated += DiscordOnChannelCreated;
        _discord.OnChannelDeleted += DiscordOnChannelDeleted;
        _discord.OnConversationSummaryUpdate += DiscordOnConversationSummaryUpdate;
        await _discord.StartWsClient();
    }

    private async Task BuildRainbet()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.RainbetEnabled]);
        if (!settings[BuiltIn.Keys.RainbetEnabled].ToBoolean())
        {
            _logger.Debug("Rainbet is disabled");
            return;
        }
        _rainbet = new RainbetWs(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _rainbet.OnRainbetBet += OnRainbetBet;
        await _rainbet.RefreshCookies();
        await _rainbet.StartWsClient();
        _logger.Info("Built Rainbet Websocket");
    }
    
    private async Task BuildChipsgg()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.ChipsggEnabled]);
        if (!settings[BuiltIn.Keys.ChipsggEnabled].ToBoolean())
        {
            _logger.Debug("Chips.gg is disabled");
            return;
        }
        _chipsgg = new Chipsgg(settings[BuiltIn.Keys.Proxy].Value);
        _chipsgg.OnChipsggRecentBet += OnChipsggRecentBet;
        await _chipsgg.StartWsClient();
        _logger.Info("Built Chips.gg Websocket connection");
    }
    
    private async Task BuildJackpot()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.JackpotEnabled]);
        if (!settings[BuiltIn.Keys.JackpotEnabled].ToBoolean())
        {
            _logger.Debug("Jackpot.bet is disabled");
            return;
        }
        var proxy = settings[BuiltIn.Keys.Proxy].Value;
        _jackpot = new Jackpot(proxy, _cancellationToken);
        _jackpot.OnJackpotBet += OnJackpotBet;
        await _jackpot.StartWsClient();
        _logger.Info("Built Jackpot Websocket connection");
    }
    
    private async Task BuildBetBolt()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.BetBoltEnabled]);
        if (!settings[BuiltIn.Keys.BetBoltEnabled].ToBoolean())
        {
            _logger.Debug("BetBolt is disabled");
            return;
        }
        _betBolt = new BetBolt(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _betBolt.OnBetBoltBet += OnBetBoltBet;
        await _betBolt.RefreshCookies();
        await _betBolt.StartWsClient();
        _logger.Info("Built BetBolt Websocket connection");
    }
    
    private async Task BuildYeet()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.YeetProxy, BuiltIn.Keys.YeetEnabled]);
        if (!settings[BuiltIn.Keys.YeetEnabled].ToBoolean())
        {
            _logger.Debug("Yeet is disabled");
            return;
        }
        _yeet = new Yeet(settings[BuiltIn.Keys.YeetProxy].Value, _cancellationToken);
        _yeet.OnYeetBet += OnYeetBet;
        _yeet.OnYeetWin += OnYeetWin;
        await _yeet.StartWsClient();
        _logger.Info("Built Yeet Websocket connection");
    }

    private async Task BuildClashgg()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.ClashggEnabled]);
        if (!settings[BuiltIn.Keys.ClashggEnabled].ToBoolean())
        {
            _logger.Debug("Clash.gg is disabled");
            return;
        }
        _clashgg = new Clashgg(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _clashgg.OnClashBet += OnClashggBet;
        await _clashgg.StartWsClient();
        _logger.Info("Built Clash.gg Websocket connection");
    }
    
    private async Task BuildTwitch()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.Proxy]);
        if (settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value == null)
        {
            _twitchDisabled = true;
            _logger.Debug($"Ignoring Twitch client as {BuiltIn.Keys.TwitchBossmanJackUsername} is not defined");
            return;
        }
        _twitch = new TwitchGraphQl(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _twitch.OnStreamStateUpdated += OnTwitchStreamStateUpdated;
        //_twitch.OnStreamCommercial += OnTwitchStreamCommercial;
        //_twitch.OnStreamTosStrike += OnTwitchStreamTosStrike;
        //await _twitch.StartWsClient();
        _twitch.StartLiveStatusCheck();
        _logger.Info("Built Twitch Websocket connection for livestream notifications");
    }

    private async Task BuildHowlgg()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.HowlggEnabled]);
        if (!settings[BuiltIn.Keys.HowlggEnabled].ToBoolean())
        {
            _logger.Debug("Howlgg is disabled");
            return;
        }
        _howlgg = new Howlgg(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _howlgg.OnHowlggBetHistory += OnHowlggBetHistory;
        await _howlgg.StartWsClient();
        _logger.Info("Built Howl.gg Websocket connection");
    }

    private async Task BuildKick()
    {
        await using var db = new ApplicationDbContext();
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.PusherEndpoint, BuiltIn.Keys.Proxy, BuiltIn.Keys.PusherReconnectTimeout,
            BuiltIn.Keys.KickEnabled
        ]);
        KickClient = new KickWsClient.KickWsClient(settings[BuiltIn.Keys.PusherEndpoint].Value!,
            settings[BuiltIn.Keys.Proxy].Value, settings[BuiltIn.Keys.PusherReconnectTimeout].ToType<int>());
        
        KickClient.OnStreamerIsLive += OnStreamerIsLive;
        KickClient.OnChatMessage += OnKickChatMessage;
        KickClient.OnWsReconnect += OnPusherWsReconnected;
        KickClient.OnPusherSubscriptionSucceeded += OnPusherSubscriptionSucceeded;
        KickClient.OnStopStreamBroadcast += OnStopStreamBroadcast;
        
        if (settings[BuiltIn.Keys.KickEnabled].ToBoolean())
        {
            await KickClient.StartWsClient();
            var kickChannels = db.Streams.Where(s => s.Service == StreamService.Kick);
            foreach (var channel in kickChannels)
            {
                if (channel.Metadata == null)
                {
                    _logger.Error($"Row ID {channel.Id} in the Streams table has null Metadata when it is required for Kick");
                    continue;
                }

                KickStreamMetaModel meta;
                try
                {
                    meta = JsonSerializer.Deserialize<KickStreamMetaModel>(channel.Metadata) ??
                           throw new InvalidOperationException(
                               $"Caught a null when attempting to deserialize metadata for {channel.Id} in the Streams table");
                }
                catch (Exception e)
                {
                    _logger.Error($"Failed to deserialize the metadata for {channel.Id} in the Streams table");
                    _logger.Error(e);
                    continue;
                }
                KickClient.SendPusherSubscribe($"channel.{meta.ChannelId}");
            }
        }
    }
    
    private async Task BuildTwitchChat()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.Proxy]);
        _logger.Debug("Building Twitch Chat");
        if (settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value == null)
        {
            _logger.Info("Not building Twitch Chat client as BMJ's username is not configured");
            return;
        }

        _bmjTwitchUsername = settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!;

        _twitchChat = new TwitchChat($"#{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value}", settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _twitchChat.OnMessageReceived += TwitchChatOnMessageReceived;
        await _twitchChat.StartWsClient();
    }
    
    private async Task BuildAlmanacShill()
    {
        AlmanacShill = new AlmanacShill(_chatBot);
        var initialState = await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotAlmanacInitialState);
        if (!initialState.ToBoolean())
        {
            _logger.Info("Built the almanac service but not enabling as initial state is false");
            return;
        }
        AlmanacShill.StartShillTask();
        _logger.Info("Built the almanac shill task");
    }

    private Task BuildDLiveStatusCheck()
    {
        _dliveStatusCheck = new DLive(_chatBot);
        _dliveStatusCheck.StartLiveStatusCheck();
        _logger.Info("Built the DLive livestream status check task");
        return Task.CompletedTask;
    }
    
    private Task BuildPeerTubeLiveStatusCheck()
    {
        _peerTubeStatusCheck = new PeerTube(_chatBot);
        _peerTubeStatusCheck.StartLiveStatusCheck();
        _logger.Info("Built the PeerTube livestream status check task");
        return Task.CompletedTask;
    }
    
    private Task BuildOwncastLiveStatusCheck()
    {
        _owncastStatusCheck = new Owncast(_chatBot);
        _owncastStatusCheck.StartLiveStatusCheck();
        _logger.Info("Built the Owncast livestream status check task");
        return Task.CompletedTask;
    }
    
    private async Task BuildParti()
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.Proxy, BuiltIn.Keys.PartiEnabled]);
        if (!settings[BuiltIn.Keys.PartiEnabled].ToBoolean())
        {
            _logger.Debug("Parti is disabled");
            return;
        }
        _parti = new Parti(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _parti.OnPartiChannelLiveNotification += OnPartiChannelLiveNotification;
        await _parti.StartWsClient();
        _logger.Info("Built Parti Websocket connection");
    }

    private async Task WebsocketWatchdog()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (_chatBot.InitialStartCooldown) continue;
            var settings = await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KickEnabled, BuiltIn.Keys.HowlggEnabled, BuiltIn.Keys.ChipsggEnabled,
                BuiltIn.Keys.ClashggEnabled, BuiltIn.Keys.BetBoltEnabled, BuiltIn.Keys.YeetEnabled,
                BuiltIn.Keys.RainbetEnabled, BuiltIn.Keys.PartiEnabled, BuiltIn.Keys.JackpotEnabled,
                BuiltIn.Keys.YouTubePubSubEnabled
            ]);
            try
            {
                if (_shuffle != null && !_shuffle.IsConnected())
                {
                    _logger.Error("Shuffle died, recreating it");
                    _shuffle.Dispose();
                    _shuffle = null!;
                    await BuildShuffle();
                }

                if (_discord != null && !_discord.IsConnected())
                {
                    _logger.Error("Discord died, recreating it");
                    _discord.Dispose();
                    _discord = null!;
                    await BuildDiscord();
                }

                if (!_twitchDisabled && _twitch != null && !_twitch.IsTaskRunning())
                {
                    _logger.Error("Twitch died, recreating it");
                    _twitch.Dispose();
                    _twitch = null!;
                    await BuildTwitch();
                }

                if (_twitchChat != null && !_twitchChat.IsConnected())
                {
                    _logger.Error("Twitch chat died, recreating it");
                    _twitchChat.Dispose();
                    _twitchChat = null!;
                    await BuildTwitchChat();
                }

                if (settings[BuiltIn.Keys.HowlggEnabled].ToBoolean() && _howlgg != null && !_howlgg.IsConnected())
                {
                    _logger.Error("Howl.gg died, recreating it");
                    _howlgg.Dispose();
                    _howlgg = null!;
                    await BuildHowlgg();
                }

                if (_jackpot != null && settings[BuiltIn.Keys.JackpotEnabled].ToBoolean() && !_jackpot.IsConnected())
                {
                    _logger.Error("Jackpot died, recreating it");
                    _jackpot.Dispose();
                    _jackpot = null!;
                    await BuildJackpot();
                }

                if (settings[BuiltIn.Keys.ChipsggEnabled].ToBoolean() && _chipsgg != null && !_chipsgg.IsConnected())
                {
                    _logger.Error("Chips died, recreating it");
                    _chipsgg.Dispose();
                    _chipsgg = null!;
                    await BuildChipsgg();
                }

                if (settings[BuiltIn.Keys.KickEnabled].ToBoolean() && KickClient != null && !KickClient.IsConnected())
                {
                    _logger.Error("Kick died, recreating it");
                    KickClient.Dispose();
                    KickClient = null!;
                    await BuildKick();
                }
                
                if (settings[BuiltIn.Keys.ClashggEnabled].ToBoolean() && _clashgg != null && !_clashgg.IsConnected())
                {
                    _logger.Error("Clash.gg died, recreating it");
                    _clashgg.Dispose();
                    _clashgg = null!;
                    await BuildClashgg();
                }
                
                if (settings[BuiltIn.Keys.BetBoltEnabled].ToBoolean() && _betBolt != null && !_betBolt.IsConnected())
                {
                    _logger.Error("BetBolt died, recreating it");
                    _betBolt.Dispose();
                    _betBolt = null!;
                    await BuildBetBolt();
                }
                
                if (settings[BuiltIn.Keys.YeetEnabled].ToBoolean() && _yeet != null && !_yeet.IsConnected())
                {
                    _logger.Error("Yeet died, recreating it");
                    _yeet.Dispose();
                    _yeet = null!;
                    await BuildYeet();
                }
                
                if (settings[BuiltIn.Keys.RainbetEnabled].ToBoolean() && _rainbet != null && !_rainbet.IsConnected())
                {
                    _logger.Error("Rainbet died, recreating it");
                    _rainbet.Dispose();
                    _rainbet = null!;
                    await BuildRainbet();
                }
                
                if (settings[BuiltIn.Keys.PartiEnabled].ToBoolean() && _parti != null && !_parti.IsConnected())
                {
                    _logger.Error("Parti died, recreating it");
                    _parti.Dispose();
                    _parti = null!;
                    await BuildParti();
                }
                
                if (_shuffleDotUs != null && !_shuffleDotUs.IsConnected())
                {
                    _logger.Error("Shuffle.us died, recreating it");
                    _shuffleDotUs.Dispose();
                    _shuffleDotUs = null!;
                    await BuildShuffleDotUs();
                }

                if (settings[BuiltIn.Keys.YouTubePubSubEnabled].ToBoolean() && _youTubePubSub != null &&
                    !_youTubePubSub.IsConnected())
                {
                    _logger.Error("YouTube PubSub died, recreating it");
                    _youTubePubSub.Dispose();
                    _youTubePubSub = null;
                    await BuildYouTubePubSub();
                }
            }
            catch (Exception e)
            {
                _logger.Error("Watchdog shit itself while trying to do something, exception follows");
                _logger.Error(e);
            }
        }
    }
    
    private async Task HowlggGetUserTimer()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (_howlgg == null || !_howlgg.IsConnected()) continue;
            var bmjUserId = await SettingsProvider.GetValueAsync(BuiltIn.Keys.HowlggBmjUserId);
            _howlgg.GetUserInfo(bmjUserId.Value!);
        }
    }
    
    private void OnRainbetBet(object sender, RainbetWsBetModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.RainbetBmjPublicIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Rainbet bet has arrived");
        using var db = new ApplicationDbContext();

        if (!settings[BuiltIn.Keys.RainbetBmjPublicIds].JsonDeserialize<List<string>>()!.Contains(bet.User.PublicId))
        {
            return;
        }
        db.RainbetBets.Add(new RainbetBetsDbModel
        {
            PublicId = bet.User.PublicId,
            RainbetUserId = bet.User.Id,
            GameName = bet.Game.Name,
            Value = float.Parse(bet.Value),
            Payout = float.Parse(bet.Payout),
            Multiplier = float.Parse(bet.Multiplier),
            BetId = bet.Id,
            UpdatedAt = bet.UpdatedAt,
            BetSeenAt = DateTimeOffset.UtcNow
        });
        _logger.Info("Added a Bossman Rainbet bet to the database");
        db.SaveChanges();
        var wagered = float.Parse(bet.Value);
        _logger.Info("ALERT BMJ IS BETTING (on Rainbet)");
        UpdateBossmanLastSighting($"betting {wagered:C} {bet.CurrencyName} on {bet.Game.Name} at Rainbet").Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;

        var payout = float.Parse(bet.Payout);
        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (payout < wagered) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage($"🚨🚨 RAINBET BETTING 🚨🚨 {settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} just bet {wagered:C} {bet.CurrencyName} which paid out " +
                                 $"[color={payoutColor}]{payout:C} {bet.CurrencyName}[/color] ({bet.Multiplier}x) on {bet.Game.Name} 💰💰", true);
    }

    private void OnJackpotBet(object sender, JackpotWsBetPayloadModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.JackpotBmjUsernames, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Jackpot bet has arrived");
        if (!settings[BuiltIn.Keys.JackpotBmjUsernames].JsonDeserialize<List<string>>()!.Contains(bet.User))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Jackpot)");
        UpdateBossmanLastSighting($"betting {bet.Wager} {bet.Currency} on {bet.GameName} at Jackpot")
            .Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;


        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Payout < bet.Wager) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage($"🚨🚨 JACKPOT BETTING 🚨🚨 {bet.User} just bet {bet.Wager} {bet.Currency} which paid out " +
                                 $"[color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", true);
    }
    
    private void OnClashggBet(object sender, ClashggBetModel bet, JsonElement jsonElement)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.ClashggBmjIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Jackpot bet has arrived");
        if (!settings[BuiltIn.Keys.ClashggBmjIds].JsonDeserialize<List<int>>()!.Contains(bet.UserId))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Clash.gg)");
        UpdateBossmanLastSighting(
                $"betting {bet.Bet / 100.0:N2} {bet.Currency.Humanize()} on {bet.Game.Humanize()} at Clash.gg")
            .Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;
        var username = settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value;
        
        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Payout < bet.Bet) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        if (bet is { Game: ClashggGame.Mines, Multiplier: <= 1 })
        {
            var mineCount = jsonElement.GetProperty("mineCount").GetInt32();
            _chatBot.SendChatMessage(
                $"🚨🚨 CLASH.GG BETTING 🚨🚨 {username} just bet {bet.Bet / 100.0:N2} {bet.Currency.Humanize()} Money on {bet.Game.Humanize()} but instantly lost with {mineCount} mines on the board. RIP 💰💰",
                true);
            return;
        }

        if (bet.Game == ClashggGame.Mines)
        {
            _chatBot.SendChatMessage($"🚨🚨 CLASH.GG BETTING 🚨🚨 {username} just bet {bet.Bet / 100.0:N2} {bet.Currency.Humanize()} Money which [b]maybe[/b] paid out " +
                                     $"{bet.Payout / 100.0:N2} {bet.Currency.Humanize()} Money ({bet.Multiplier}x) on {bet.Game.Humanize()} 💰💰", true);
            return;
        }
        _chatBot.SendChatMessage($"🚨🚨 CLASH.GG BETTING 🚨🚨 {username} just bet {bet.Bet / 100.0:N2} {bet.Currency.Humanize()} Money which paid out " +
                                 $"[color={payoutColor}]{bet.Payout / 100.0:N2} {bet.Currency.Humanize()} Money[/color] ({bet.Multiplier}x) on {bet.Game.Humanize()} 💰💰", true);
    }
    
    private void OnBetBoltBet(object sender, BetBoltBetModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.BetBoltBmjUsernames, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("BetBolt bet has arrived");
        if (!settings[BuiltIn.Keys.BetBoltBmjUsernames].JsonDeserialize<List<string>>()!.Contains(bet.Username))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on BetBolt)");
        UpdateBossmanLastSighting($"betting {bet.BetAmountFiat:C} on {bet.GameName} at BetBolt").Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;
        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.WinAmountFiat < 0) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage($"🚨🚨 JEETBOLT BETTING 🚨🚨 {bet.Username} just bet {bet.BetAmountFiat:C} ({bet.BetAmountCrypto:N2} {bet.Crypto}) and won " +
                                 $"[color={payoutColor}]{bet.WinAmountFiat:C} ({bet.WinAmountCrypto:N2} {bet.Crypto})[/color] ({bet.Multiplier:N2}x) on {bet.GameName} 💩💩", true);
    }
    
    private void OnYeetBet(object sender, YeetCasinoBetModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.YeetBmjUsernames, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Yeet bet has arrived");
        if (!settings[BuiltIn.Keys.YeetBmjUsernames].JsonDeserialize<List<string>>()!.Contains(bet.Username))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Yeet)");
        UpdateBossmanLastSighting($"betting {bet.BetAmount:C} on {bet.GameName} at Yeet").Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;
        //if (bet.WinAmountFiat < 0) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        var msg = _chatBot.SendChatMessage($"🚨🚨 JEET BETTING 🚨🚨 {bet.Username} just bet {bet.BetAmount:C} worth of {bet.CurrencyCode} on {bet.GameName} 💩💩", true);
        _yeetBets.Add(bet.BetIdentifier, new SeenYeetBet {Bet = bet, Message = msg});
    }
    
    private void OnYeetWin(object sender, YeetCasinoWinModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.YeetBmjUsernames, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Yeet bet has arrived");
        if (!settings[BuiltIn.Keys.YeetBmjUsernames].JsonDeserialize<List<string>>()!.Contains(bet.Username))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Yeet)");
        UpdateBossmanLastSighting($"betting {bet.BetAmount:C} on {bet.GameName} at Yeet").Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;
        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Multiplier < 1) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        var newMsg =
            $"🚨🚨 JEET BETTING 🚨🚨 {bet.Username} just bet {bet.BetAmount:C} worth of {bet.CurrencyCode} and got " +
            $"[color={payoutColor}]{bet.WinAmount:C}[/color] ({bet.Multiplier:N2}x) on {bet.GameName} 💩💩";
        if (!_yeetBets.ContainsKey(bet.BetIdentifier) || (DateTimeOffset.UtcNow - _yeetBets[bet.BetIdentifier].Bet.CreatedAt).TotalSeconds > 30)
        {
            _logger.Error($"Could not correlate {bet.BetIdentifier} to a previously sent bet message (restarted?) or old bet message is old as hell (feach?). Sending win as-is");
            _chatBot.SendChatMessage(newMsg, true);
            return;
        }

        var oldMsg = _yeetBets[bet.BetIdentifier];
        if (oldMsg.Message.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost)
        {
            _logger.Error($"{bet.BetIdentifier} was lost, sending as-is");
            _chatBot.SendChatMessage(newMsg, true);
            return;
        }

        // Can't block the event as otherwise it'll lag out the whole Yeet client
        _ = OnYeetWinEditTaskAsync(oldMsg.Message, newMsg);
    }

    private async Task OnYeetWinEditTaskAsync(SentMessageTrackerModel oldMsg, string newMsg)
    {
        var i = 0;
        while (oldMsg.ChatMessageId == null && i < 50)
        {
            await Task.Delay(100, _cancellationToken);
            i++;
        }

        if (oldMsg.ChatMessageId == null)
        {
            _logger.Error($"Timed out waiting to figure out our message ID");
            return;
        }

        await _chatBot.KfClient.EditMessageAsync(oldMsg.ChatMessageId.Value, newMsg);
    }
    
    private void OnHowlggBetHistory(object sender, HowlggBetHistoryResponseModel data)
    {
        _logger.Debug("Received bet history from Howl.gg");
        using var db = new ApplicationDbContext();
        foreach (var bet in data.History.Data)
        {
            // Slot feature buys have an unrealized value that means they show no profit until the feature finishes
            // The feed will return the correct profit later hence updating the values
            var existingBet = db.HowlggBets.FirstOrDefault(b => b.BetId == bet.Id);
            if (existingBet != null)
            {
                _logger.Trace("Bet already exists in DB");
                if (existingBet.Bet == bet.Bet && existingBet.Profit == bet.Profit) continue;
                _logger.Debug("Updating fields");
                existingBet.Bet = bet.Bet;
                existingBet.Profit = bet.Profit;
                db.SaveChanges();
                continue;
            }

            db.HowlggBets.Add(new HowlggBetsDbModel
            {
                UserId = data.User.Id,
                BetId = bet.Id,
                GameId = bet.GameId,
                Bet = bet.Bet,
                Profit = bet.Profit,
                Date = bet.Date,
                Game = bet.Game
            });
            _logger.Info("Added bet to DB");
        }

        db.SaveChanges();
    }
    
    private void DiscordOnConversationSummaryUpdate(object sender, DiscordConversationSummaryUpdateModel summary, string guildId)
    {
        _logger.Info($"Received a conversation summary update for guild {guildId}");
        var settings = SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.DiscordIcon, BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordOnlySendSummariesIncludingBmj,
            BuiltIn.Keys.DiscordDisableConversationSummaries
        ]).Result;
        if (settings[BuiltIn.Keys.DiscordDisableConversationSummaries].ToBoolean())
        {
            return;
        }
        var discordIcon = settings[BuiltIn.Keys.DiscordIcon];
        if (settings[BuiltIn.Keys.DiscordOnlySendSummariesIncludingBmj].ToBoolean() &&
            !summary.People.Contains(settings[BuiltIn.Keys.DiscordBmjId].Value ?? string.Empty))
        {
            _logger.Info($"Ignoring as BMJ's Discord ID '{settings[BuiltIn.Keys.DiscordBmjId].Value}' wasn't among the people listed for this conversation summary: {string.Join(", ", summary.People)}");
            return;
        }
        _chatBot.SendChatMessage($"[img]{discordIcon.Value}[/img] {summary.Topic}: {summary.SummaryShort} 🤖🤖", true);
    }
    
    private void DiscordOnChannelDeleted(object sender, DiscordChannelDeletionModel channel)
    {
        _logger.Info($"Received channel deletion event of type {channel.Type} with name {channel.Name}");
        if (channel.Type != DiscordChannelType.GuildText && channel.Type != DiscordChannelType.GuildVoice &&
            channel.Type != DiscordChannelType.GuildStageVoice) return;
        var discordIcon = SettingsProvider.GetValueAsync(BuiltIn.Keys.DiscordIcon).Result;
        var channelName = channel.Name ?? "Unknown name";
        _chatBot.SendChatMessage($"[img]{discordIcon.Value}[/img] Discord {channel.Type.Humanize()} channel '{channelName}' was deleted 🚨🚨", true);
        UpdateBossmanLastSighting($"deleting {channelName} on Discord").Wait(_cancellationToken);
    }

    private void DiscordOnChannelCreated(object sender, DiscordChannelCreationModel channel)
    {
        _logger.Info($"Received channel creation event of type {channel.Type} with name {channel.Name}");
        if (channel.Type != DiscordChannelType.GuildText && channel.Type != DiscordChannelType.GuildVoice &&
            channel.Type != DiscordChannelType.GuildStageVoice) return;
        var discordIcon = SettingsProvider.GetValueAsync(BuiltIn.Keys.DiscordIcon).Result;
        var channelName = channel.Name ?? "Unknown name";
        _chatBot.SendChatMessage($"[img]{discordIcon.Value}[/img] New Discord {channel.Type.Humanize()} channel created: {channelName} 🚨🚨", true);
        UpdateBossmanLastSighting($"creating {channelName} on Discord").Wait(_cancellationToken);
    }
    
    private void TwitchChatOnMessageReceived(object sender, string nick, string target, string message)
    {
        if (nick != _bmjTwitchUsername)
        {
            return;
        }
        // Not caching this value as it won't harm it to have to look this up in even the worst spergout sesh
        var twitchIcon = SettingsProvider.GetValueAsync(BuiltIn.Keys.TwitchIcon).Result.Value;
        _chatBot.SendChatMessage($"[img]{twitchIcon}[/img] {nick}: {message.TrimEnd('\r')}", true);
        UpdateBossmanLastSighting("talking in Twitch chat").Wait(_cancellationToken);
    }

    // TODO: Figure out why this never works
    private void DiscordOnPresenceUpdated(object sender, DiscordPresenceUpdateModel presence)
    {
        var settings = SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordIcon]).Result;
        if (presence.User.Id != settings[BuiltIn.Keys.DiscordBmjId].Value)
        {
            return;
        }
        // if (_lastDiscordStatus == presence.Status)
        // {
        //     _logger.Debug("Ignoring status update as it's the same as the last one");
        //     return;
        // }
        // _lastDiscordStatus = presence.Status;
        var clientStatus = presence.ClientStatus.Keys.Aggregate(string.Empty, (current, device) => current + $"{device} is {presence.ClientStatus[device]}; ");
        _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {presence.User.GlobalName ?? presence.User.Username} has updated his Discord presence: {clientStatus}");
        UpdateBossmanLastSighting($"going {presence.Status} on Discord").Wait(_cancellationToken);
    }

    private void DiscordOnMessageReceived(object sender, DiscordMessageModel message)
    {
        var settings = SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordIcon]).Result;
        if (message.Author.Id != settings[BuiltIn.Keys.DiscordBmjId].Value)
        {
            return;
        }

        if (message.Type == DiscordMessageType.StageStart)
        {
            var db = new ApplicationDbContext();
            var winman = db.Images.Where(i => i.Key == "winmanjack").ToList().OrderBy(i => i.LastSeen).Take(1)
                .ToList();
            var liveMessage =
                $"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username} just started a stage called {message.Content} 🚨🚨" +
                $"[br]🚨🚨 {message.Author.GlobalName ?? message.Author.Username} is [b]LIVE[/b] on Discord! 🚨🚨";
            if (winman.Count > 0)
            {
                liveMessage += $"[br][img]{winman[0].Url}[/img]";
            }
            var bmt = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset);

            _chatBot.SendChatMessage(liveMessage,
                true);
            var flashMsg =
                _chatBot.SendChatMessage($"Verified [b]True and Honest[/b] by @KenoGPT at {bmt:dddd h:mm:ss tt} BMT",
                    true);
            _ = DiscordFlashText(flashMsg);
            UpdateBossmanLastSighting("going live on Discord").Wait(_cancellationToken);
            return;
        }
        if (message.Type == DiscordMessageType.StageEnd)
        {
            _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username} just ended a stage called {message.Content} :lossmanjack:",
                true);
            UpdateBossmanLastSighting("ending a stage on Discord").Wait(_cancellationToken);
            return;
        }
        
        var result = $"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username}: {message.Content?.Replace("❤️", ":feels:")}";
        foreach (var attachment in message.Attachments ?? [])
        {
            result += $"[br]Attachment: {attachment.GetProperty("filename").GetString()} {attachment.GetProperty("url").GetString()}";
        }
        
        _chatBot.SendChatMessage(result, TemporarilyBypassGambaSeshForDiscord);
        UpdateBossmanLastSighting("talking in Discord").Wait(_cancellationToken);
    }

    private async Task DiscordFlashText(SentMessageTrackerModel msg)
    {
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsRedColor, BuiltIn.Keys.KiwiFarmsGreenColor
            ]);
        var patience = 0;
        while (msg.ChatMessageId == null)
        {
            patience++;
            if (msg.Status is SentMessageTrackerStatus.Lost or SentMessageTrackerStatus.NotSending || patience > 50)
            {
                _logger.Error($"Message '{msg.Message}' got lost/blackholed or we gave up waiting");
                return;
            }

            await Task.Delay(125, _cancellationToken);
        }

        var seconds = 0;
        while (seconds < 60)
        {
            if (seconds % 2 == 0)
            {
                await _chatBot.KfClient.EditMessageAsync(msg.ChatMessageId.Value, $"[color={settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]{msg.Message}[/color]");
            }
            else
            {
                await _chatBot.KfClient.EditMessageAsync(msg.ChatMessageId.Value, $"[color={settings[BuiltIn.Keys.KiwiFarmsRedColor].Value}]{msg.Message}[/color]");
            }

            await Task.Delay(1000, _cancellationToken);
            seconds++;
        }
    }

    private void DiscordOnInvalidCredentials(object sender, DiscordPacketReadModel packet)
    {
        _logger.Error("Credentials failed to validate.");
    }

    private void ShuffleOnLatestBetUpdated(object sender, ShuffleLatestBetModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.ShuffleBmjUsername, BuiltIn.Keys.ShuffleDotUsBmjUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Shuffle bet has arrived");
        bool isDotUs;
        if (bet.Username == settings[BuiltIn.Keys.ShuffleBmjUsername].Value)
        {
            isDotUs = false;
            UpdateBossmanLastSighting($"betting {bet.Amount} {bet.Currency} on {bet.GameName} at Shuffle.com").Wait(_cancellationToken);
        }
        else if (bet.Username == settings[BuiltIn.Keys.ShuffleDotUsBmjUsername].Value)
        {
            isDotUs = true;
            UpdateBossmanLastSighting($"betting {bet.Amount} {bet.Currency} on {bet.GameName} at Shuffle.us").Wait(_cancellationToken);
        }
        else
        {
            return;
        }
        _logger.Info($"ALERT BMJ IS BETTING: isDotUs => {isDotUs}");
        if (CheckBmjIsLive().Result) return;

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (float.Parse(bet.Payout) < float.Parse(bet.Amount)) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        var preamble = "🚨🚨 Shufflebros! 🚨🚨";
        if (isDotUs)
        {
            preamble = "🦅🦅 Shuffle US! 🦅🦅";
        }
        // There will be a check for live status but ignoring that while we deal with an emergency dice situation
        _chatBot.SendChatMessage($"{preamble} {bet.Username} just bet {bet.Amount} {bet.Currency} which paid out [color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", true);
    }

    private void OnTwitchStreamStateUpdated(object sender, string channelName, bool isLive)
    {
        _logger.Info($"BossmanJack stream event came in. isLive => {isLive}");
        var settings = SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.CaptureEnabled, BuiltIn.Keys.TwitchIcon, BuiltIn.Keys.CaptureStreamlinkBmjWorkingDirectory
        ]).Result;
        var bmjUsername = settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value;

        if (isLive)
        {
            _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.TwitchIcon].Value}[/img] {bmjUsername} just went [b]LIVE[/b] on Twitch! https://www.twitch.tv/{bmjUsername} 🚨🚨", true);
            if (settings[BuiltIn.Keys.CaptureEnabled].ToBoolean())
            {
                _logger.Info("Capturing Bossman's stream");
                _ = new StreamCapture($"https://www.twitch.tv/{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value}",
                    StreamCaptureMethods.Streamlink,
                    new CaptureOverridesModel
                    {
                        CaptureYtDlpWorkingDirectory = settings[BuiltIn.Keys.CaptureStreamlinkBmjWorkingDirectory].Value
                    }, _cancellationToken).CaptureAsync();
            }

            UpdateBossmanLastSighting("going live on Twitch").Wait(_cancellationToken);
            return;
        }
        _chatBot.SendChatMessage($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} is no longer live! :lossmanjack:", true);
        UpdateBossmanLastSighting("ending stream on Twitch").Wait(_cancellationToken);
    }
    
    private void OnChipsggRecentBet(object sender, ChipsggBetModel bet)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.ChipsggBmjUserIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Chips.gg bet has arrived");
        if (!settings[BuiltIn.Keys.ChipsggBmjUserIds].JsonDeserialize<List<string>>()!.Contains(bet.UserId ?? "0"))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Chips.gg)");
        using var db = new ApplicationDbContext();
        db.ChipsggBets.Add(new ChipsggBetDbModel
        {
            Created = bet.Created, Updated = bet.Updated, UserId = bet.UserId ?? "0", Username = bet.Username ?? "Unknown", Win = bet.Win,
            Winnings = bet.Winnings, GameTitle = bet.GameTitle!, Amount = bet.Amount, Multiplier = bet.Multiplier,
            Currency = bet.Currency!, CurrencyPrice = bet.CurrencyPrice, BetId = bet.BetId ?? "0"
        });
        db.SaveChanges();
        UpdateBossmanLastSighting($"betting {bet.Amount:N} {bet.Currency!.ToUpper()} on {bet.GameTitle} at Chips.gg")
            .Wait(_cancellationToken);
        if (CheckBmjIsLive().Result) return;

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Winnings < bet.Amount) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage(
            $"🚨🚨 CHIPS BROS 🚨🚨 {settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} just bet [plain]{bet.Amount:N} {bet.Currency!.ToUpper()} " +
            $"({bet.Amount * bet.CurrencyPrice:C}) which paid out [/plain][color={payoutColor}][plain]{bet.Winnings:N} {bet.Currency.ToUpper()} " +
            $"({bet.Winnings * bet.CurrencyPrice:C})[/plain][/color] [plain]({bet.Multiplier:N}x) on {bet.GameTitle}[/plain] 💰💰",
            true);
    }
    
    private void OnPusherWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Pusher reconnected due to {reconnectionInfo.Type}");
        using var db = new ApplicationDbContext();
        var kickChannels = db.Streams.Where(s => s.Service == StreamService.Kick);
        foreach (var channel in kickChannels)
        {
            if (channel.Metadata == null)
            {
                _logger.Error($"Row ID {channel.Id} in the Streams table has null Metadata when it is required for Kick");
                continue;
            }

            KickStreamMetaModel meta;
            try
            {
                meta = JsonSerializer.Deserialize<KickStreamMetaModel>(channel.Metadata) ??
                       throw new InvalidOperationException(
                           $"Caught a null when attempting to deserialize metadata for {channel.Id} in the Streams table");
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to deserialize the metadata for {channel.Id} in the Streams table");
                _logger.Error(e);
                continue;
            }
            KickClient?.SendPusherSubscribe($"channel.{meta.ChannelId}");
        }
    }

    private void OnPusherSubscriptionSucceeded(object sender, PusherModels.BasePusherEventModel? e)
    {
        _logger.Info($"Pusher indicates subscription to {e?.Channel} was successful");
    }
    
    private void OnKickChatMessage(object sender, KickModels.ChatMessageEventModel? e)
    {
        if (e == null) return; 
        _logger.Debug($"BB Code Translation: {e.Content.TranslateKickEmotes()}");

        if (e.Sender.Slug != "bossmanjack") return;
        var kickIcon = SettingsProvider.GetValueAsync(BuiltIn.Keys.KickIcon).Result;
        
        _logger.Debug("Message from BossmanJack");
        _chatBot.SendChatMessage($"[img]{kickIcon.Value}[/img] BossmanJack: {e.Content.TranslateKickEmotes()}");
        UpdateBossmanLastSighting("talking in Kick chat").Wait(_cancellationToken);
    }
    
    private void OnStreamerIsLive(object sender, KickModels.StreamerIsLiveEventModel? e)
    {
        if (e == null) return;
        var settings = SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.CaptureEnabled
        ]).Result;
        using var db = new ApplicationDbContext();
        var channels = db.Streams.Where(s => s.Service == StreamService.Kick).Include(s => s.User);
        StreamDbModel? channel = null;
        KickStreamMetaModel? meta = null;
        foreach (var ch in channels)
        {
            if (ch.Metadata == null)
            {
                _logger.Error($"Row ID {ch.Id} in the Streams table has null Metadata when it is required for Kick");
                continue;
            }

            try
            {
                meta = JsonSerializer.Deserialize<KickStreamMetaModel>(ch.Metadata) ??
                       throw new InvalidOperationException(
                           $"Caught a null when attempting to deserialize metadata for {ch.Id} in the Streams table");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to deserialize the metadata for {ch.Id} in the Streams table");
                _logger.Error(ex);
                continue;
            }

            if (meta.ChannelId != e.Livestream.ChannelId) continue;
            
            channel = ch;
            break;
        }

        if (channel == null)
        {
            _logger.Error($"Failed to find a Kick stream in the database for {e.Livestream.ChannelId} which we got notified is live");
            _logger.Error("This really should never happen, but could happen if the metadata for a stream gets screwed up at runtime");
            return;
        }

        var identity = "A streamer";
        if (channel.User != null)
        {
            identity = "@" + channel.User.KfUsername;
        }
        _chatBot.SendChatMessage(
            $"{identity} is live! {e.Livestream.SessionTitle} {channel.StreamUrl}", true);

        if (channel.AutoCapture && settings[BuiltIn.Keys.CaptureEnabled].ToBoolean())
        {
            _logger.Info($"{channel.StreamUrl} is configured to auto capture");
            _ = new StreamCapture(channel.StreamUrl, StreamCaptureMethods.YtDlp, meta?.CaptureOverrides, _cancellationToken).CaptureAsync();
        }
    }
    
    private void OnPartiChannelLiveNotification(object sender, PartiChannelLiveNotificationModel data)
    {
        var settings = SettingsProvider
            .GetMultipleValuesAsync([BuiltIn.Keys.CaptureEnabled]).Result;
        using var db = new ApplicationDbContext();
        var url = $"https://parti.com/creator/{data.SocialMedia}/{data.Username}/";
        if (data.SocialMedia == "discord")
        {
            url += "0";
        }

        var channel = db.Streams.Include(s => s.User)
            .FirstOrDefault(s => s.Service == StreamService.Parti && s.StreamUrl == url);
        if (channel == null)
        {
            _logger.Info($"Got a live notification from Parti for a stream we don't care about: {data.SocialMedia}/{data.Username}");
            return;
        }
        var identity = "A streamer";
        if (channel.User != null)
        {
            identity = "@" + channel.User.KfUsername;
        }

        BaseMetaModel? meta = null;
        if (channel.Metadata != null)
        {
            try
            {
                meta = JsonSerializer.Deserialize<BaseMetaModel>(channel.Metadata);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to deserialize metadata for Parti stream: {channel.StreamUrl}");
                _logger.Error(e);
            }
        }
        
        _chatBot.SendChatMessage($"{identity} is live! {data.EventTitle} {url}", true);
        if (channel.AutoCapture && settings[BuiltIn.Keys.CaptureEnabled].ToBoolean())
        {
            _logger.Info($"{channel.StreamUrl} is configured to auto capture");
            _ = new StreamCapture(url, StreamCaptureMethods.YtDlp, meta?.CaptureOverrides, _cancellationToken).CaptureAsync();
        }
    }

    private void OnStopStreamBroadcast(object sender, KickModels.StopStreamBroadcastEventModel? e)
    {
        if (e == null) return;
        using var db = new ApplicationDbContext();
        var channels = db.Streams.Where(s => s.Service == StreamService.Kick).Include(s => s.User);
        StreamDbModel? channel = null;
        foreach (var ch in channels)
        {
            if (ch.Metadata == null)
            {
                _logger.Error($"Row ID {ch.Id} in the Streams table has null Metadata when it is required for Kick");
                continue;
            }

            KickStreamMetaModel meta;
            try
            {
                meta = JsonSerializer.Deserialize<KickStreamMetaModel>(ch.Metadata) ??
                       throw new InvalidOperationException(
                           $"Caught a null when attempting to deserialize metadata for {ch.Id} in the Streams table");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to deserialize the metadata for {ch.Id} in the Streams table");
                _logger.Error(ex);
                continue;
            }

            if (meta.ChannelId != e.Livestream.Id) continue;
            
            channel = ch;
            break;
        }

        if (channel == null)
        {
            _logger.Error($"Failed to find a Kick stream in the database for {e.Livestream.Id} which we got notified is no longer live");
            _logger.Error("This really should never happen, but could happen if the metadata for a stream gets screwed up at runtime");
            return;
        }

        var identity = "A streamer";
        if (channel.User != null)
        {
            identity = "@" + channel.User.KfUsername;
        }

        _chatBot.SendChatMessage(
            $"{identity} is no longer live! :lossmanjack:", true);
    }

    public async Task<bool> CheckBmjIsLive()
    {
        if (TemporarilyForceGambaMessages) return false;
        var isLive =
            (await SettingsProvider.GetValueAsync(BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive)).ToBoolean();
        if (isLive || TemporarilySuppressGambaMessages)
        {
            return true;
        }
        return false;
    }

    public async Task UpdateBossmanLastSighting(string activity)
    {
        _logger.Info($"Updating Bossman last sighting with: {activity}");
        var sighting = new LastSightingModel
        {
            When = DateTimeOffset.UtcNow,
            Activity = activity
        };
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.BossmanLastSighting, sighting);
    }
    
    private void YouTubePubSubOnNewVideo(object sender, YouTubePubSubNotificationModel data)
    {
        var video = YouTubeApi.GetVideoDetails(data.Id).Result;
        if (video == null)
        {
            _logger.Error($"Caught a null when getting video details for {data.Id} which we were just notified about");
            return;
        }

        if (video.Snippet.LiveBroadcastContent == "live")
        {
            _chatBot.SendChatMessageAsync($"@{video.Snippet.ChannelTitle} has gone live! {data.Title} {data.Url}", true).Wait(_cancellationToken);
        }
        else if (video.Snippet.LiveBroadcastContent == "upcoming")
        {
            _chatBot.SendChatMessageAsync($"@{video.Snippet.ChannelTitle} has scheduled a live stream! {data.Title} {data.Url}", true).Wait(_cancellationToken);
        }
        else if (video.Snippet.LiveBroadcastContent == "none")
        {
            _chatBot.SendChatMessageAsync($"@{video.Snippet.ChannelTitle} has uploaded a new video! {data.Title} {data.Url}", true).Wait(_cancellationToken);
        }
        else
        {
            _logger.Error($"YouTube live broadcast content '{video.Snippet.LiveBroadcastContent}' was unhandled for {data.Id}");
        }
    }
}
