﻿using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using KickWsClient.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot;

public class ChatBot
{
    internal readonly ChatClient KfClient;
    private readonly KickWsClient.KickWsClient _kickClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly bool _pingEnabled = true;
    internal bool GambaSeshPresent;
    private string _xfSessionToken = null!;
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private readonly List<int> _seenMsgIds = [];
    // Suppresses the command handler on initial start, so it doesn't pick up things already handled on restart
    private bool _initialStartCooldown = true;
    private readonly CancellationToken _cancellationToken = new();
    private Twitch _twitch;
    private Shuffle _shuffle;
    private DiscordService _discord;
    private TwitchChat _twitchChat;
    private string? _lastDiscordStatus;
    internal bool IsBmjLive = false;
    private bool _isBmjLiveSynced = false;
    private DateTime _lastKfEvent = DateTime.Now;
    private BotCommands _botCommands;
    private string _bmjTwitchUsername;
    private Howlgg _howlgg;
    private bool _kickDisabled = true;
    private bool _twitchDisabled = false;
    private Task _websocketWatchdog;
    private Jackpot _jackpot;
    private Rainbet _rainbet;
    
    public ChatBot()
    {
        _logger.Info("Bot starting!");

        var settings = Helpers.GetMultipleValues([
            BuiltIn.Keys.KiwiFarmsWsEndpoint, BuiltIn.Keys.KiwiFarmsDomain, BuiltIn.Keys.PusherEndpoint,
            BuiltIn.Keys.Proxy, BuiltIn.Keys.PusherReconnectTimeout, BuiltIn.Keys.PusherChannels,
            BuiltIn.Keys.TwitchBossmanJackId, BuiltIn.Keys.DiscordToken, BuiltIn.Keys.KiwiFarmsWsReconnectTimeout,
            BuiltIn.Keys.KiwiFarmsToken, BuiltIn.Keys.KickEnabled
        ]).Result;

        _xfSessionToken = settings[BuiltIn.Keys.KiwiFarmsToken].Value ?? "unset";
        if (_xfSessionToken == "unset")
        {
            RefreshXfToken().Wait(_cancellationToken);
        }
        
        KfClient = new ChatClient(new ChatClientConfigModel
        {
            WsUri = new Uri(settings[BuiltIn.Keys.KiwiFarmsWsEndpoint].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsWsEndpoint} cannot be null")),
            XfSessionToken = _xfSessionToken,
            CookieDomain = settings[BuiltIn.Keys.KiwiFarmsDomain].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsDomain} cannot be null"),
            Proxy = settings[BuiltIn.Keys.Proxy].Value,
            ReconnectTimeout = Convert.ToInt32(settings[BuiltIn.Keys.KiwiFarmsWsReconnectTimeout].Value)
        });

        _kickClient = new KickWsClient.KickWsClient(settings[BuiltIn.Keys.PusherEndpoint].Value!,
            settings[BuiltIn.Keys.Proxy].Value, Convert.ToInt32(settings[BuiltIn.Keys.PusherReconnectTimeout].Value));
        
        _logger.Debug("Creating bot command instance");
        _botCommands = new BotCommands(this, _cancellationToken);

        KfClient.OnMessages += OnKfChatMessage;
        KfClient.OnUsersParted += OnUsersParted;
        KfClient.OnUsersJoined += OnUsersJoined;
        KfClient.OnWsDisconnection += OnKfWsDisconnected;
        KfClient.OnWsReconnect += OnKfWsReconnected;
        KfClient.OnFailedToJoinRoom += OnFailedToJoinRoom;

        _kickClient.OnStreamerIsLive += OnStreamerIsLive;
        _kickClient.OnChatMessage += OnKickChatMessage;
        _kickClient.OnWsReconnect += OnPusherWsReconnected;
        _kickClient.OnPusherSubscriptionSucceeded += OnPusherSubscriptionSucceeded;
        _kickClient.OnStopStreamBroadcast += OnStopStreamBroadcast;
        
        KfClient.StartWsClient().Wait(_cancellationToken);

        if (settings[BuiltIn.Keys.KickEnabled].ToBoolean())
        {
            _kickClient.StartWsClient().Wait(_cancellationToken);
            var pusherChannels = settings[BuiltIn.Keys.PusherChannels].Value ?? "";
            foreach (var channel in pusherChannels.Split(','))
            {
                _kickClient.SendPusherSubscribe(channel);
            }

            _kickDisabled = false;
        }

        _logger.Debug("Creating ping thread and starting it");
        var pingThread = new Thread(PingThread);
        pingThread.Start();

        if (settings[BuiltIn.Keys.TwitchBossmanJackId].Value != null)
        {
            _logger.Debug("Creating Twitch live stream notification client");
            BuildTwitch();
        }
        else
        {
            _twitchDisabled = true;
            _logger.Debug($"Ignoring Twitch client as {BuiltIn.Keys.TwitchBossmanJackId} is not defined");
        }

        BuildShuffle();
        BuildDiscord();
        BuildTwitchChat();
        BuildHowlgg();
        BuildJackpot();
        BuildRainbet();
        
        _logger.Info("Starting websocket watchdog");
        _websocketWatchdog = WebsocketWatchdog();

        _logger.Debug("Blocking the main thread");
        var exitEvent = new ManualResetEvent(false);
        exitEvent.WaitOne();
    }

    private async Task WebsocketWatchdog()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (_initialStartCooldown) continue;
            try
            {
                if (!_shuffle.IsConnected())
                {
                    _logger.Error("Shuffle died, recreating it");
                    _shuffle.Dispose();
                    _shuffle = null!;
                    BuildShuffle();
                }

                if (!_discord.IsConnected())
                {
                    _logger.Error("Discord died, recreating it");
                    _discord.Dispose();
                    _discord = null!;
                    BuildDiscord();
                }

                if (!_twitchDisabled && !_twitch.IsConnected())
                {
                    _logger.Error("Twitch died, recreating it");
                    _twitch.Dispose();
                    _twitch = null!;
                    BuildTwitch();
                }

                if (!_twitchChat.IsConnected())
                {
                    _logger.Error("Twitch chat died, recreating it");
                    _twitchChat.Dispose();
                    _twitchChat = null!;
                    BuildTwitchChat();
                }

                if (!_howlgg.IsConnected())
                {
                    _logger.Error("Howl.gg died, recreating it");
                    _howlgg.Dispose();
                    _howlgg = null!;
                    BuildHowlgg();
                }

                if (!_jackpot.IsConnected())
                {
                    _logger.Error("Jackpot died, recreating it");
                    _jackpot.Dispose();
                    _jackpot = null!;
                    BuildJackpot();
                }
            }
            catch (Exception e)
            {
                _logger.Error("Watchdog shit itself while trying to do something, exception follows");
                _logger.Error(e);
            }

        }
    }
    
    private void BuildRainbet()
    {
        _rainbet = new Rainbet(_cancellationToken);
        _rainbet.OnRainbetBet += OnRainbetBet;
        _rainbet.StartGameHistoryTimer();
    }

    private void OnRainbetBet(object sender, List<RainbetBetHistoryModel> bets)
    {
        var settings = Helpers
            .GetMultipleValues([
                BuiltIn.Keys.RainbetBmjPublicId, BuiltIn.Keys.TwitchBossmanJackUsername,
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]).Result;
        _logger.Trace("Rainbet bet has arrived");
        using var db = new ApplicationDbContext();
        foreach (var bet in bets.Where(b => b.User.PublicId == settings[BuiltIn.Keys.RainbetBmjPublicId].Value))
        //foreach (var bet in bets)
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
                Payout = bet.Payout,
                Multiplier = bet.Multiplier,
                BetId = bet.Id,
                UpdatedAt = bet.UpdatedAt,
                BetSeenAt = DateTimeOffset.UtcNow
            });
            _logger.Info("Added a Bossman Rainbet bet to the database");
        }

        db.SaveChanges();
    }

    private void BuildJackpot()
    {
        var proxy = Helpers.GetValue(BuiltIn.Keys.Proxy).Result.Value;
        _jackpot = new Jackpot(proxy, _cancellationToken);
        _jackpot.OnJackpotBet += OnJackpotBet;
        _jackpot.StartWsClient().Wait(_cancellationToken);
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
        if (IsBmjLive)
        {
            _logger.Info("Ignoring as BMJ is live");
            return;
        }

        // Only check once because the bot should be tracking the Twitch stream
        // This is just in case he's already live while the bot starts
        // He was schizo betting on Dice, so I want to avoid a lot of API requests to Twitch in case they rate limit
        if (!_isBmjLiveSynced)
        {
            IsBmjLive = _twitch.IsStreamLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!).Result;
            _isBmjLiveSynced = true;
        }
        if (IsBmjLive)
        {
            _logger.Info("Double checked and he is really online");
            return;
        }

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (bet.Payout < bet.Wager) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        // There will be a check for live status but ignoring that while we deal with an emergency dice situation
        SendChatMessage($"🚨🚨 JACKPOT BETTING 🚨🚨 {bet.User} just bet {bet.Wager} {bet.Currency} which paid out [color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", false);
    }

    public void BuildTwitch()
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.TwitchBossmanJackId, BuiltIn.Keys.Proxy]).Result;
        _twitch = new Twitch([Convert.ToInt32(settings[BuiltIn.Keys.TwitchBossmanJackId].Value)], settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _twitch.OnStreamStateUpdated += OnTwitchStreamStateUpdated;
        _twitch.StartWsClient().Wait(_cancellationToken);
    }

    private void BuildHowlgg()
    {
        var proxy = Helpers.GetValue(BuiltIn.Keys.Proxy).Result.Value;
        _howlgg = new Howlgg(proxy, _cancellationToken);
        _howlgg.OnHowlggBetHistory += OnHowlggBetHistory;
        _howlgg.StartWsClient().Wait(_cancellationToken);
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

    private void BuildShuffle()
    {
        _logger.Debug("Building Shuffle");
        _shuffle = new Shuffle(Helpers.GetValue(BuiltIn.Keys.Proxy).Result.Value, _cancellationToken);
        _shuffle.OnLatestBetUpdated += ShuffleOnLatestBetUpdated;
        _shuffle.StartWsClient().Wait(_cancellationToken);
    }

    private void BuildDiscord()
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.DiscordToken, BuiltIn.Keys.Proxy]).Result;
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
        _discord.StartWsClient().Wait(_cancellationToken);
    }

    private void BuildTwitchChat()
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.Proxy]).Result;
        _logger.Debug("Building Twitch Chat");
        if (settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value == null)
        {
            _logger.Info("Not building Twitch Chat client as BMJ's username is not configured");
            return;
        }

        _bmjTwitchUsername = settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!;

        _twitchChat = new TwitchChat($"#{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value}", settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        _twitchChat.OnMessageReceived += TwitchChatOnMessageReceived;
        _twitchChat.StartWsClient().Wait(_cancellationToken);
    }
    
    private void TwitchChatOnMessageReceived(object sender, string nick, string target, string message)
    {
        if (nick != _bmjTwitchUsername)
        {
            return;
        }
        // Not caching this value as it won't harm it to have to look this up in even the worst spergout sesh
        var twitchIcon = Helpers.GetValue(BuiltIn.Keys.TwitchIcon).Result.Value;
        SendChatMessage($"[img]{twitchIcon}[/img] {nick}: {message}", true);
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
        SendChatMessage($"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] BossmanJack has updated his Discord presence: {clientStatus}");
    }

    private void DiscordOnMessageReceived(object sender, DiscordMessageModel message)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.DiscordBmjId, BuiltIn.Keys.DiscordIcon]).Result;
        if (message.Author.Id != settings[BuiltIn.Keys.DiscordBmjId].Value)
        {
            return;
        }
        
        var result = $"[img]{settings[BuiltIn.Keys.DiscordIcon].Value}[/img] BossmanJack: {message.Content}";
        foreach (var attachment in message.Attachments ?? [])
        {
            result += $"[br]Attachment: {attachment.GetProperty("filename").GetString()} {attachment.GetProperty("url").GetString()}";
        }
        SendChatMessage(result);
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
        if (IsBmjLive)
        {
            _logger.Info("Ignoring as BMJ is live");
            return;
        }

        // Only check once because the bot should be tracking the Twitch stream
        // This is just in case he's already live while the bot starts
        // He was schizo betting on Dice, so I want to avoid a lot of API requests to Twitch in case they rate limit
        if (!_isBmjLiveSynced)
        {
            IsBmjLive = _twitch.IsStreamLive(settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value!).Result;
            _isBmjLiveSynced = true;
        }
        if (IsBmjLive)
        {
            _logger.Info("Double checked and he is really online");
            return;
        }

        var payoutColor = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        if (float.Parse(bet.Payout) < float.Parse(bet.Amount)) payoutColor = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        // There will be a check for live status but ignoring that while we deal with an emergency dice situation
        SendChatMessage($"🚨🚨 Shufflebros! 🚨🚨 {bet.Username} just bet {bet.Amount} {bet.Currency} which paid out [color={payoutColor}]{bet.Payout} {bet.Currency}[/color] ({bet.Multiplier}x) on {bet.GameName} 💰💰", true);
    }

    private void OnTwitchStreamStateUpdated(object sender, int channelId, bool isLive)
    {
        _logger.Info($"BossmanJack stream event came in. isLive => {isLive}");
        if (isLive)
        {
            SendChatMessage("BossmanJack just went live on Twitch! https://www.twitch.tv/thebossmanjack\r\n" +
                             "Ad-free re-stream at https://bossmanjack.tv courtesy of @Kees H");
            IsBmjLive = true;
            return;
        }
        SendChatMessage("BossmanJack is no longer live! :lossmanjack:");
        IsBmjLive = false;
    }

    private void OnFailedToJoinRoom(object sender, string message)
    {
        _logger.Error($"Couldn't join the room. KF returned: {message}");
        _logger.Error("This is likely due to the session cookie expiring. Retrieving a new one.");
        RefreshXfToken().Wait(_cancellationToken);
        KfClient.UpdateToken(_xfSessionToken);
        _logger.Info("Retrieved fresh token. Reconnecting.");
        KfClient.Disconnect();
        KfClient.StartWsClient().Wait(_cancellationToken);
        _logger.Info("Client should be reconnecting now");
    }

    private void PingThread()
    {
        while (_pingEnabled)
        {
            Thread.Sleep(TimeSpan.FromSeconds(15));
            _logger.Debug("Pinging KF");
            KfClient.SendMessage("/ping");
            if (!_kickDisabled)
            {
                _kickClient.SendPusherPing();
            }
            if (_initialStartCooldown) _initialStartCooldown = false;
            var inactivityTime = DateTime.Now - _lastKfEvent;
            _logger.Debug($"Last KF event was {inactivityTime:g} ago");
            if (inactivityTime.TotalMinutes > 10)
            {
                _logger.Error("Forcing reconnection as bot is completely dead");
                KfClient.Reconnect().Wait(_cancellationToken);
            }
            _logger.Debug("Polling Bossman's Howl.gg stats");
            _howlgg?.GetUserInfo("951905");
        }
    }

    private async Task RefreshXfToken()
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.KiwiFarmsDomain,
        BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.KiwiFarmsPassword, BuiltIn.Keys.KiwiFarmsChromiumPath,
        BuiltIn.Keys.Proxy]).Result;
        var cookie = await KfTokenService.FetchSessionTokenAsync(settings[BuiltIn.Keys.KiwiFarmsDomain].Value!,
            settings[BuiltIn.Keys.KiwiFarmsUsername].Value!, settings[BuiltIn.Keys.KiwiFarmsPassword].Value!,
            settings[BuiltIn.Keys.KiwiFarmsChromiumPath].Value!, settings[BuiltIn.Keys.Proxy].Value);
        _logger.Debug($"FetchSessionTokenAsync returned {cookie}");
        _xfSessionToken = cookie;
        await Helpers.SetValue(BuiltIn.Keys.KiwiFarmsToken, _xfSessionToken);
    }

    private void OnStreamerIsLive(object sender, KickModels.StreamerIsLiveEventModel? e)
    {
        if (e == null) return;
        SendChatMessage($"Dirt Devils LFG! @Juhlonduss is live! {e.Livestream.SessionTitle} https://kick.com/dirtdevil-enjoyer", true);
    }

    private void OnStopStreamBroadcast(object sender, KickModels.StopStreamBroadcastEventModel? e)
    {
        SendChatMessage("Dirt Devils felted. Stream is over. :lossmanjack:", true);
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshDetectEnabled, BuiltIn.Keys.GambaSeshUserId])
            .Result;
        _lastKfEvent = DateTime.Now;
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            _logger.Info($"KF ({message.MessageDate.ToLocalTime():HH:mm:ss}) <{message.Author.Username}> {message.Message}");
            if (settings[BuiltIn.Keys.GambaSeshDetectEnabled].Value == "true" && !_initialStartCooldown && message.Author.Id == Convert.ToInt32(settings[BuiltIn.Keys.GambaSeshUserId].Value) && !GambaSeshPresent)
            {
                _logger.Info("Received a GambaSesh message after cooldown and while thinking he's not here. Setting the presence flag to avoid spamming chat");
                GambaSeshPresent = true;
            }
            if (!_seenMsgIds.Contains(message.MessageId) && !_initialStartCooldown)
            {
                _logger.Debug("Passing message to command interface");
                _botCommands.ProcessMessage(message);
            }
            else
            {
                _logger.Debug($"_seenMsgIds check => {!_seenMsgIds.Contains(message.MessageId)}, _initialStartCooldown => {_initialStartCooldown}");
            }
            _seenMsgIds.Add(message.MessageId);
        }
    }

    public void SendChatMessage(string message, bool bypassSeshDetect = false)
    {
        var settings = Helpers
            .GetMultipleValues([BuiltIn.Keys.KiwiFarmsSuppressChatMessages, BuiltIn.Keys.GambaSeshDetectEnabled])
            .Result;
        if (settings[BuiltIn.Keys.KiwiFarmsSuppressChatMessages].Value == "true")
        {
            _logger.Info("Not sending message as SuppressChatMessages is enabled");
            _logger.Info($"Message was: {message}");
            return;
        }
        if (GambaSeshPresent && settings[BuiltIn.Keys.GambaSeshDetectEnabled].Value == "true" && !bypassSeshDetect)
        {
            _logger.Info($"Not sending message '{message}' as GambaSesh is present");
            return;
        }

        KfClient.SendMessage(message);
    }

    private void OnKickChatMessage(object sender, KickModels.ChatMessageEventModel? e)
    {
        if (e == null) return; 
        _logger.Info($"Kick ({e.CreatedAt.LocalDateTime.ToLocalTime():HH:mm:ss}) <{e.Sender.Username}> {e.Content}");
        _logger.Debug($"BB Code Translation: {e.Content.TranslateKickEmotes()}");

        if (e.Sender.Slug != "bossmanjack") return;
        var kickIcon = Helpers.GetValue(BuiltIn.Keys.KickIcon).Result;
        
        _logger.Debug("Message from BossmanJack");
        SendChatMessage($"[img]{kickIcon.Value}[/img] BossmanJack: {e.Content.TranslateKickEmotes()}");
    }
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled])
            .Result;
        _lastKfEvent = DateTime.Now;
        _logger.Debug($"Received {users.Count} user join events");
        using var db = new ApplicationDbContext();
        foreach (var user in users)
        {
            if (user.Id == Convert.ToInt32(settings[BuiltIn.Keys.GambaSeshUserId].Value) && settings[BuiltIn.Keys.GambaSeshDetectEnabled].Value == "true")
            {
                _logger.Info("GambaSesh is now present");
                GambaSeshPresent = true;
            }
            _logger.Info($"{user.Username} joined!");

            var userDb = db.Users.FirstOrDefault(u => u.KfId == user.Id);
            if (userDb == null)
            {
                db.Users.Add(new UserDbModel { KfId = user.Id, KfUsername = user.Username });
                _logger.Debug("Adding user to DB");
                continue;
            }
            // Detect a username change
            if (userDb.KfUsername != user.Username)
            {
                _logger.Debug("Username has updated, updating DB");
                userDb.KfUsername = user.Username;
                db.SaveChanges();
            }
        }

        db.SaveChanges();
    }

    private void OnUsersParted(object sender, List<int> userIds)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled])
            .Result;
        _lastKfEvent = DateTime.Now;
        if (userIds.Contains(Convert.ToInt32(settings[BuiltIn.Keys.GambaSeshUserId].Value)) && settings[BuiltIn.Keys.GambaSeshDetectEnabled].Value == "true")
        {
            _logger.Info("GambaSesh is no longer present");
            GambaSeshPresent = false;
        }
    }

    private void OnKfWsDisconnected(object sender, DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Sneedchat disconnected due to {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
    }
    
    private void OnKfWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        var roomId = Convert.ToInt32(Helpers.GetValue(BuiltIn.Keys.KiwiFarmsRoomId).Result.Value);
        _logger.Error($"Sneedchat reconnected due to {reconnectionInfo.Type}");
        _logger.Info("Resetting GambaSesh presence so it can resync if he crashed while the bot was DC'd");
        GambaSeshPresent = false;
        _logger.Info($"Rejoining {roomId}");
        KfClient.JoinRoom(roomId);
    }
    
    private void OnPusherWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Pusher reconnected due to {reconnectionInfo.Type}");
        var channels = Helpers.GetValue(BuiltIn.Keys.PusherChannels).Result.Value ?? "";
        foreach (var channel in channels.Split(','))
        {
            _logger.Info($"Rejoining {channel}");
            _kickClient.SendPusherSubscribe(channel);
        }
    }

    private void OnPusherSubscriptionSucceeded(object sender, PusherModels.BasePusherEventModel? e)
    {
        _logger.Info($"Pusher indicates subscription to {e?.Channel} was successful");
    }
}