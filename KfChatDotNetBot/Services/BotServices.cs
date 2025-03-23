using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KickWsClient.Models;
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
    
    internal KickWsClient.KickWsClient KickClient;
    private Twitch _twitch;
    private Shuffle _shuffle;
    private DiscordService _discord;
    private TwitchChat _twitchChat;
    private Jackpot _jackpot;
    private Howlgg _howlgg;
    private Rainbet _rainbet;
    private Chipsgg _chipsgg;
    private Clashgg _clashgg;
    public AlmanacShill AlmanacShill;
    
    private Task? _websocketWatchdog;
    private Task? _howlggGetUserTimer;
    
    private string _bmjTwitchUsername;
    private bool _twitchDisabled = false;
    private string? _lastDiscordStatus;
    internal bool IsBmjLive = false;
    private bool _isBmjLiveSynced = false;
    internal bool IsChrisDjLive = false;
    
    // lol
    internal bool TemporarilyBypassGambaSeshForDiscord;
    internal bool TemporarilySuppressGambaMessages = false;

    public BotServices(ChatBot botInstance, CancellationToken ctx)
    {
        _chatBot = botInstance;
        _cancellationToken = ctx;
        TemporarilyBypassGambaSeshForDiscord =
            Helpers.GetValue(BuiltIn.Keys.DiscordTemporarilyBypassGambaSeshInitialValue).Result.ToBoolean();
        
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
            BuildAlmanacShill()
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

        BuildRainbet();
        
        _logger.Info("Starting websocket watchdog and Howl.gg user stats timer");
        _websocketWatchdog = WebsocketWatchdog();
        _howlggGetUserTimer = HowlggGetUserTimer();
    }
    
    private async Task BuildShuffle()
    {
        _logger.Debug("Building Shuffle");
        _shuffle = new Shuffle((await Helpers.GetValue(BuiltIn.Keys.Proxy)).Value, _cancellationToken);
        _shuffle.OnLatestBetUpdated += ShuffleOnLatestBetUpdated;
        await _shuffle.StartWsClient();
    }

    private async Task BuildDiscord()
    {
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.DiscordToken, BuiltIn.Keys.Proxy]);
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
        await _discord.StartWsClient();
    }
    
    private void BuildRainbet()
    {
        _rainbet = new Rainbet(_cancellationToken);
        _rainbet.OnRainbetBet += OnRainbetBet;
        _rainbet.StartGameHistoryTimer();
        _logger.Info("Built Rainbet timer");
    }
    
    private async Task BuildChipsgg()
    {
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.Proxy, BuiltIn.Keys.ChipsggEnabled]);
        if (!settings[BuiltIn.Keys.ChipsggEnabled].ToBoolean())
        {
            _logger.Debug("Chips.gg is disabled");
            return;
        }
        _chipsgg = new Chipsgg(settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _chipsgg.OnChipsggRecentBet += OnChipsggRecentBet;
        await _chipsgg.StartWsClient();
        _logger.Info("Built Chips.gg Websocket connection");
    }
    
    private async Task BuildJackpot()
    {
        var proxy = Helpers.GetValue(BuiltIn.Keys.Proxy).Result.Value;
        _jackpot = new Jackpot(proxy, _cancellationToken);
        _jackpot.OnJackpotBet += OnJackpotBet;
        await _jackpot.StartWsClient();
        _logger.Info("Built Jackpot Websocket connection");
    }
    
    private async Task BuildClashgg()
    {
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.Proxy, BuiltIn.Keys.ClashggEnabled]);
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
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.TwitchBossmanJackId, BuiltIn.Keys.Proxy]);
        if (settings[BuiltIn.Keys.TwitchBossmanJackId].Value == null)
        {
            _twitchDisabled = true;
            _logger.Debug($"Ignoring Twitch client as {BuiltIn.Keys.TwitchBossmanJackId} is not defined");
            return;
        }
        _twitch = new Twitch([settings[BuiltIn.Keys.TwitchBossmanJackId].ToType<int>()], settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _twitch.OnStreamStateUpdated += OnTwitchStreamStateUpdated;
        _twitch.OnStreamCommercial += OnTwitchStreamCommercial;
        await _twitch.StartWsClient();
        _logger.Info("Built Twitch Websocket connection for livestream notifications");
    }

    private async Task BuildHowlgg()
    {
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.Proxy, BuiltIn.Keys.HowlggEnabled]);
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
        var settings = await Helpers.GetMultipleValues([
            BuiltIn.Keys.PusherEndpoint, BuiltIn.Keys.Proxy, BuiltIn.Keys.PusherReconnectTimeout, BuiltIn.Keys.KickEnabled,
            BuiltIn.Keys.PusherChannels, BuiltIn.Keys.KickChannels
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
            // var pusherChannels = settings[BuiltIn.Keys.PusherChannels].ToList();
            // foreach (var channel in pusherChannels)
            // {
            //     _kickClient.SendPusherSubscribe(channel);
            // }
            var kickChannels = settings[BuiltIn.Keys.KickChannels].JsonDeserialize<List<KickChannelModel>>();
            if (kickChannels == null) return;
            foreach (var channel in kickChannels)
            {
                KickClient.SendPusherSubscribe($"channel.{channel.ChannelId}");
            }
        }
    }
    
    private async Task BuildTwitchChat()
    {
        var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.Proxy]);
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
        var initialState = await Helpers.GetValue(BuiltIn.Keys.BotAlmanacInitialState);
        if (!initialState.ToBoolean())
        {
            _logger.Info("Built the almanac service but not enabling as initial state is false");
            return;
        }
        AlmanacShill.StartShillTask();
        _logger.Info("Built the almanac shill task");
    }
    
    private async Task WebsocketWatchdog()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (_chatBot.InitialStartCooldown) continue;
            var settings = await Helpers.GetMultipleValues([BuiltIn.Keys.KickEnabled, BuiltIn.Keys.HowlggEnabled, BuiltIn.Keys.ChipsggEnabled, BuiltIn.Keys.ClashggEnabled]);
            try
            {
                if (!_shuffle.IsConnected())
                {
                    _logger.Error("Shuffle died, recreating it");
                    _shuffle.Dispose();
                    _shuffle = null!;
                    await BuildShuffle();
                }

                if (!_discord.IsConnected())
                {
                    _logger.Error("Discord died, recreating it");
                    _discord.Dispose();
                    _discord = null!;
                    await BuildDiscord();
                }

                if (!_twitchDisabled && !_twitch.IsConnected())
                {
                    _logger.Error("Twitch died, recreating it");
                    _twitch.Dispose();
                    _twitch = null!;
                    await BuildTwitch();
                }

                if (!_twitchChat.IsConnected())
                {
                    _logger.Error("Twitch chat died, recreating it");
                    _twitchChat.Dispose();
                    _twitchChat = null!;
                    await BuildTwitchChat();
                }

                if (settings[BuiltIn.Keys.HowlggEnabled].ToBoolean() && !_howlgg.IsConnected())
                {
                    _logger.Error("Howl.gg died, recreating it");
                    _howlgg.Dispose();
                    _howlgg = null!;
                    await BuildHowlgg();
                }

                if (!_jackpot.IsConnected())
                {
                    _logger.Error("Jackpot died, recreating it");
                    _jackpot.Dispose();
                    _jackpot = null!;
                    await BuildJackpot();
                }

                if (settings[BuiltIn.Keys.ChipsggEnabled].ToBoolean() && !_chipsgg.IsConnected())
                {
                    _logger.Error("Chips died, recreating it");
                    _chipsgg.Dispose();
                    _chipsgg = null!;
                    await BuildChipsgg();
                }

                if (settings[BuiltIn.Keys.KickEnabled].ToBoolean() && !KickClient.IsConnected())
                {
                    _logger.Error("Kick died, recreating it");
                    KickClient.Dispose();
                    KickClient = null!;
                    await BuildKick();
                }
                
                if (settings[BuiltIn.Keys.ClashggEnabled].ToBoolean() && !_clashgg.IsConnected())
                {
                    _logger.Error("Clash.gg died, recreating it");
                    _clashgg.Dispose();
                    _clashgg = null!;
                    await BuildClashgg();
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
            var bmjUserId = await Helpers.GetValue(BuiltIn.Keys.HowlggBmjUserId);
            _howlgg.GetUserInfo(bmjUserId.Value!);
        }
    }
    
    private void OnRainbetBet(object sender, List<RainbetBetHistoryModel> bets)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.RainbetBmjPublicIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Rainbet bet has arrived");
        using var db = new ApplicationDbContext();
        var ids = settings[BuiltIn.Keys.RainbetBmjPublicIds].JsonDeserialize<List<string>>();
        if (ids == null)
        {
            _logger.Error("BMJ Rainbet Public IDs were null");
            return;
        }

        var bmjBets = bets.Where(b => b.User.PublicId != null && ids.Contains(b.User.PublicId));
        if (!bmjBets.Any())
        {
            return;
        }
        foreach (var bet in bmjBets)
        {
            if (db.RainbetBets.Any(b => b.BetId == bet.Id))
            {
                _logger.Trace($"Ignoring bet {bet.Id} as we've already logged it");
                continue;
            }

            db.RainbetBets.Add(new RainbetBetsDbModel
            {
                PublicId = bet.User.PublicId,
                RainbetUserId = bet.User.Id,
                GameName = bet.Game.Name,
                Value = bet.Value,
                Payout = bet.Payout ?? 0,
                Multiplier = bet.Multiplier ?? 0,
                BetId = bet.Id,
                UpdatedAt = bet.UpdatedAt,
                BetSeenAt = DateTimeOffset.UtcNow
            });
            _logger.Info("Added a Bossman Rainbet bet to the database");
        }
        db.SaveChanges();
        
        _logger.Info("ALERT BMJ IS BETTING (on Rainbet)");
        if (CheckBmjIsLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value ?? "usernamenotset").Result) return;
        
        var msg = $":!::!: {settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} is betting on Rainbet :!::!:";

        foreach (var bet in bmjBets.GroupBy(b => b.Game.Name))
        {
            var wagered = bet.Sum(s => s.Value);
            var payout = bet.Sum(s => s.Payout);
            var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
            if (payout < wagered) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
            msg += $"[br]{bet.Sum(s => s.Value):C} wagered on {bet.Key} which paid out [color={payoutColor}]{payout:C}[/color] over {bet.Count()} bets";
        }

        _chatBot.SendChatMessagesAsync(msg.FancySplitMessage(partSeparator: "[br]"), true).Wait(_cancellationToken);
    }

    private void OnJackpotBet(object sender, JackpotWsBetPayloadModel bet)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.JackpotBmjUsername, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Jackpot bet has arrived");
        if (bet.User != settings[BuiltIn.Keys.JackpotBmjUsername].Value)
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Jackpot)");
        if (CheckBmjIsLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value ?? "usernamenotset").Result) return;


        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Payout < bet.Wager) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage($"🚨🚨 JACKPOT BETTING 🚨🚨 {bet.User} just bet {bet.Wager} {bet.Currency} which paid out " +
                                 $"[color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", true);
    }
    
    private void OnClashggBet(object sender, ClashggBetModel bet)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.ClashggBmjIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Jackpot bet has arrived");
        if (!settings[BuiltIn.Keys.ClashggBmjIds].JsonDeserialize<List<int>>()!.Contains(bet.UserId))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Clash.gg)");
        if (CheckBmjIsLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value ?? "usernamenotset").Result) return;

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Payout < bet.Bet) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage($"🚨🚨 CLASH.GG BETTING 🚨🚨 austingambles just bet {bet.Bet / 100} {bet.Currency.Humanize()} Money which paid out " +
                                 $"[color={payoutColor}]{bet.Payout / 100} {bet.Currency.Humanize()} Money[/color] ({bet.Multiplier}x) on {bet.Game.Humanize()} 💰💰", true);
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
    
    private void DiscordOnChannelDeleted(object sender, DiscordChannelDeletionModel channel)
    {
        _logger.Info($"Received channel deletion event of type {channel.Type} with name {channel.Name}");
        if (channel.Type != DiscordChannelType.GuildText && channel.Type != DiscordChannelType.GuildVoice &&
            channel.Type != DiscordChannelType.GuildStageVoice) return;
        var discordIcon = Helpers.GetValue(BuiltIn.Keys.DiscordIcon).Result;
        var channelName = channel.Name ?? "Unknown name";
        _chatBot.SendChatMessage($"[img]{discordIcon.Value}[/img] Discord {channel.Type.Humanize()} channel '{channelName}' was deleted 🚨🚨", true);
    }

    private void DiscordOnChannelCreated(object sender, DiscordChannelCreationModel channel)
    {
        _logger.Info($"Received channel creation event of type {channel.Type} with name {channel.Name}");
        if (channel.Type != DiscordChannelType.GuildText && channel.Type != DiscordChannelType.GuildVoice &&
            channel.Type != DiscordChannelType.GuildStageVoice) return;
        var discordIcon = Helpers.GetValue(BuiltIn.Keys.DiscordIcon).Result;
        var channelName = channel.Name ?? "Unknown name";
        _chatBot.SendChatMessage($"[img]{discordIcon.Value}[/img] New Discord {channel.Type.Humanize()} channel created: {channelName} 🚨🚨", true);
    }
    
    private void TwitchChatOnMessageReceived(object sender, string nick, string target, string message)
    {
        if (nick != _bmjTwitchUsername)
        {
            return;
        }
        // Not caching this value as it won't harm it to have to look this up in even the worst spergout sesh
        var twitchIcon = Helpers.GetValue(BuiltIn.Keys.TwitchIcon).Result.Value;
        _chatBot.SendChatMessage($"[img]{twitchIcon}[/img] {nick}: {message.TrimEnd('\r')}", true);
    }

    private void DiscordOnPresenceUpdated(object sender, DiscordPresenceUpdateModel presence)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordIcon]).Result;
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
    }

    private void DiscordOnMessageReceived(object sender, DiscordMessageModel message)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordIcon]).Result;
        if (message.Author.Id != settings[BuiltIn.Keys.DiscordBmjId].Value)
        {
            return;
        }

        if (message.Type == DiscordMessageType.StageStart)
        {
            _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username} just started a stage called {message.Content} 🚨🚨" +
                                     $"[br]🚨🚨 {message.Author.GlobalName ?? message.Author.Username} is [b]LIVE[/b] on Discord! 🚨🚨",
                true);
            return;
        }
        if (message.Type == DiscordMessageType.StageEnd)
        {
            _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username} just ended a stage called {message.Content} :lossmanjack:",
                true);
            return;
        }
        
        var result = $"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] {message.Author.GlobalName ?? message.Author.Username}: {message.Content}";
        foreach (var attachment in message.Attachments ?? [])
        {
            result += $"[br]Attachment: {attachment.GetProperty("filename").GetString()} {attachment.GetProperty("url").GetString()}";
        }
        
        _chatBot.SendChatMessage(result, TemporarilyBypassGambaSeshForDiscord);
    }

    private void DiscordOnInvalidCredentials(object sender, DiscordPacketReadModel packet)
    {
        _logger.Error("Credentials failed to validate.");
    }

    private void ShuffleOnLatestBetUpdated(object sender, ShuffleLatestBetModel bet)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.ShuffleBmjUsername, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Shuffle bet has arrived");
        if (bet.Username != settings[BuiltIn.Keys.ShuffleBmjUsername].Value)
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING");
        if (CheckBmjIsLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value ?? "usernamenotset").Result) return;

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (float.Parse(bet.Payout) < float.Parse(bet.Amount)) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        // There will be a check for live status but ignoring that while we deal with an emergency dice situation
        _chatBot.SendChatMessage($"🚨🚨 Shufflebros! 🚨🚨 {bet.Username} just bet {bet.Amount} {bet.Currency} which paid out [color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", true);
    }

    private void OnTwitchStreamStateUpdated(object sender, int channelId, bool isLive)
    {
        _logger.Info($"BossmanJack stream event came in. isLive => {isLive}");
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.RestreamUrl, BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.BotToyStoryImage]).Result;

        if (isLive)
        {
            _chatBot.SendChatMessage($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} just went live on Twitch! https://www.twitch.tv/{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value}\r\n" +
                                     settings[BuiltIn.Keys.RestreamUrl].Value);
            if (IsChrisDjLive)
            {
                _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.BotToyStoryImage].Value}[/img]", true);
            }
            IsBmjLive = true;
            return;
        }
        _chatBot.SendChatMessage($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} is no longer live! :lossmanjack:");
        IsBmjLive = false;
    }
    
    private void OnTwitchStreamCommercial(object sender, int channelId, int length, bool scheduled)
    {
        var settings = Helpers
            .GetMultipleValues([BuiltIn.Keys.TwitchShillRestreamOnCommercial, BuiltIn.Keys.TwitchCommercialRestreamShillMessage, BuiltIn.Keys.TwitchBossmanJackUsername]).Result;
        if (!settings[BuiltIn.Keys.TwitchShillRestreamOnCommercial].ToBoolean())
        {
            _logger.Debug("Not shilling as it's disabled");
            return;
        }

        _chatBot.SendChatMessage(
            $"Did you just get a {length} second ad on Twitch? The Keno Kasino encourages Total Advertiser Death.[br]" +
            $"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} streams are being re-streamed in low latency, ad-free form thanks to @Kees H. Do not watch ads.[br]{settings[BuiltIn.Keys.TwitchCommercialRestreamShillMessage].Value}",
            true);
    }
    
    private void OnChipsggRecentBet(object sender, ChipsggBetModel bet)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.ChipsggBmjUserIds, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Chips.gg bet has arrived");
        if (!settings[BuiltIn.Keys.ChipsggBmjUserIds].ToList().Contains(bet.UserId))
        {
            return;
        }
        _logger.Info("ALERT BMJ IS BETTING (on Chips.gg)");
        using var db = new ApplicationDbContext();
        db.ChipsggBets.Add(new ChipsggBetDbModel
        {
            Created = bet.Created, Updated = bet.Updated, UserId = bet.UserId, Username = bet.Username ?? "Unknown", Win = bet.Win,
            Winnings = bet.Winnings, GameTitle = bet.GameTitle!, Amount = bet.Amount, Multiplier = bet.Multiplier,
            Currency = bet.Currency!, CurrencyPrice = bet.CurrencyPrice, BetId = bet.BetId
        });
        db.SaveChanges();
        if (CheckBmjIsLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value ?? "usernamenotset").Result) return;

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Winnings < bet.Amount) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        _chatBot.SendChatMessage(
            $"🚨🚨 CHIPS BROS 🚨🚨 AustinGambles just bet [plain]{bet.Amount:N} {bet.Currency!.ToUpper()} " +
            $"({bet.Amount * bet.CurrencyPrice:C}) which paid out [/plain][color={payoutColor}][plain]{bet.Winnings:N} {bet.Currency.ToUpper()} " +
            $"({bet.Winnings * bet.CurrencyPrice:C})[/plain][/color] [plain]({bet.Multiplier:N}x) on {bet.GameTitle}[/plain] 💰💰",
            true);
    }
    
    private void OnPusherWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Pusher reconnected due to {reconnectionInfo.Type}");
        var kickChannels = Helpers.GetValue(BuiltIn.Keys.KickChannels).Result.JsonDeserialize<List<KickChannelModel>>();
        if (kickChannels == null) return;
        foreach (var channel in kickChannels)
        {
            KickClient.SendPusherSubscribe($"channel.{channel.ChannelId}");
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
        var kickIcon = Helpers.GetValue(BuiltIn.Keys.KickIcon).Result;
        
        _logger.Debug("Message from BossmanJack");
        _chatBot.SendChatMessage($"[img]{kickIcon.Value}[/img] BossmanJack: {e.Content.TranslateKickEmotes()}");
    }
    
    private void OnStreamerIsLive(object sender, KickModels.StreamerIsLiveEventModel? e)
    {
        if (e == null) return;
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.KickChannels, BuiltIn.Keys.BotChrisDjLiveImage]).Result;
        var channels = settings[BuiltIn.Keys.KickChannels].JsonDeserialize<List<KickChannelModel>>();
        if (channels == null)
        {
            _logger.Error("Caught null when grabbing Kick channels");
            return;
        }

        var channel = channels.FirstOrDefault(ch => ch.ChannelId == e.Livestream.ChannelId);
        if (channel == null)
        {
            _logger.Error($"Caught null when grabbing channel data for {e.Livestream.ChannelId}");
            return;
        }

        using var db = new ApplicationDbContext();
        var user = db.Users.FirstOrDefault(u => u.KfId == channel.ForumId);
        if (user == null)
        {
            _logger.Error($"Caught null when retrieving forum user {channel.ForumId}");
            return;
        }

        _chatBot.SendChatMessage(
            $"@{user.KfUsername} is live! {e.Livestream.SessionTitle} https://kick.com/{channel.ChannelSlug}", true);
        
        if (channel.ChannelSlug == "christopherdj")
        {
            IsChrisDjLive = true;
            _chatBot.SendChatMessage($"[img]{settings[BuiltIn.Keys.BotChrisDjLiveImage].Value}[/img]", true);
        }
    }

    private void OnStopStreamBroadcast(object sender, KickModels.StopStreamBroadcastEventModel? e)
    {
        if (e == null) return;
        var channels = Helpers.GetValue(BuiltIn.Keys.KickChannels).Result.JsonDeserialize<List<KickChannelModel>>();
        if (channels == null)
        {
            _logger.Error("Caught null when grabbing Kick channels");
            return;
        }

        var channel = channels.FirstOrDefault(ch => ch.ChannelId == e.Livestream.Channel.Id);
        if (channel == null)
        {
            _logger.Error($"Caught null when grabbing channel data for {e.Livestream.Channel.Id}");
            return;
        }

        using var db = new ApplicationDbContext();
        var user = db.Users.FirstOrDefault(u => u.KfId == channel.ForumId);
        if (user == null)
        {
            _logger.Error($"Caught null when retrieving forum user {channel.ForumId}");
            return;
        }

        _chatBot.SendChatMessage(
            $"@{user.KfUsername} is no longer live! :lossmanjack:", true);
        if (channel.ChannelSlug == "christopherdj") IsChrisDjLive = false;
    }

    public async Task<bool> CheckBmjIsLive(string bmjUsername)
    {
        if (IsBmjLive)
        {
            return true;
        }
        if (TemporarilySuppressGambaMessages)
        {
            _logger.Info("Ignoring as TemporarilySuppressGambaMessages is true");
            return true;
        }
        // Only check once because the bot should be tracking the Twitch stream
        // This is just in case he's already live while the bot starts
        // He was schizo betting on Dice, so I want to avoid a lot of API requests to Twitch in case they rate limit
        if (!_isBmjLiveSynced)
        {
            IsBmjLive = await _twitch.IsStreamLive(bmjUsername);
            _isBmjLiveSynced = true;
        }
        if (IsBmjLive)
        {
            _logger.Info("Double checked and he is really online");
            return true;
        }

        return false;
    }
}