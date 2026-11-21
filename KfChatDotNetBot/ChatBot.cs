using System.Text.Json;
using EphyraChatClient;
using EphyraChatClient.Models;
using EphyraChatClient.Models.Events;
using Homoglyphic;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot;

public class ChatBot
{
    /// <summary>
    /// Everything to do with whispers is marked [Obsolete] with this message so the compiler points out each usage
    /// </summary>
    public const string WhispersGoneMessage =
        "Ephyra-Chat has no whispers. This does nothing now and is only still here so its usages can be found and cleaned up.";

    internal readonly EphyraClient KfClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private readonly List<SeenMessageMetadataModel> _seenMessages = [];
    // Set until the bot has joined the room for the first time. Other services use it to hold off until chat is usable
    internal bool InitialStartCooldown = true;
    private readonly CancellationToken _cancellationToken = new();
    private readonly BotCommands _botCommands;
    public readonly List<SentMessageTrackerModel> SentMessages = [];
    internal bool GambaSeshPresent;
    internal readonly BotServices BotServices;
    private Task _kfChatWatchdog;
    private KfTokenService _kfTokenService;
    private int _joinFailures = 0;
    private Task _kfDeadBotDetection;
    // When the watchdog last kicked the chat client. Only there to stop it doing that more than once a minute
    private DateTime _lastReconnectAttempt = DateTime.UtcNow;
    // When the bot was last seen in working order (see IsChatAlive). This is what the dead bot detection goes off
    private DateTime _lastAlive = DateTime.UtcNow;
    private List<ScheduledAutoDeleteModel> _scheduledDeletions = [];
    private Task _scheduledAutoDeleteTask;
    private List<UserModel> _currentUsersInChat = [];
    private readonly Lock _currentUsersLock = new();
    private HomoglyphSearch? _homoglyphSearch;
    /// <summary>
    /// UUID of the room the bot lives in (KiwiFarms.RoomUuid). Null if it hasn't been configured
    /// </summary>
    internal readonly string? RoomUuid;
    // Principal UUID -> user, for everyone whose forum ID we've managed to work out
    private readonly Dictionary<string, UserModel> _knownUsers = new(StringComparer.OrdinalIgnoreCase);
    // Principal UUID -> when a whois for them last came up empty, so we don't hammer the forum over the same person
    private readonly Dictionary<string, DateTime> _whoisFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _userResolveLock = new();

    public ChatBot()
    {
        _logger.Info("Bot starting!");

        _logger.Debug("Starting services");
        BotServices = new BotServices(this, _cancellationToken);
        BotServices.InitializeServices();
        
        _kfDeadBotDetection = KfDeadBotDetectionTask();
        var settings = SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsChatEndpoint, BuiltIn.Keys.KiwiFarmsDomain,
            BuiltIn.Keys.KiwiFarmsProxy, BuiltIn.Keys.KiwiFarmsRoomUuid]).Result;

        var domain = settings[BuiltIn.Keys.KiwiFarmsDomain].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsDomain} cannot be null");
        _kfTokenService = new KfTokenService(domain, settings[BuiltIn.Keys.KiwiFarmsProxy].Value, _cancellationToken);

        if (_kfTokenService.GetCookies().Count == 0)
        {
            try
            {
                RefreshXfToken().Wait(_cancellationToken);
            }
            catch (Exception e)
            {
                _logger.Error("Caught an exception while trying to refresh the XF token");
                _logger.Error(e);
            }
        }

        KfClient = new EphyraClient(new EphyraClientConfigModel
        {
            ChatEndpoint = new Uri(settings[BuiltIn.Keys.KiwiFarmsChatEndpoint].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsChatEndpoint} cannot be null")),
            ForumBaseUri = new Uri($"https://{domain}"),
            // If there's a chat session cookie left over from the last run in here, the bot gets straight back in
            Cookies = _kfTokenService.GetCookies(),
            CookieDomain = domain,
            Proxy = settings[BuiltIn.Keys.KiwiFarmsProxy].Value,
            // The chat host calls on this whenever it stops accepting our session. Going through the token service
            // means KiwiFlare and logging back into the forum are taken care of
            TicketProvider = GetChatTicketAsync,
            // And this when KiwiFlare answers the chat with a 203 because it wants its challenge solved
            ChallengeSolver = SolveChatChallengeAsync
        });
  
        _logger.Debug("Creating bot command instance");
        _botCommands = new BotCommands(this, _cancellationToken);

        KfClient.OnMessagePosted += OnKfChatMessage;
        KfClient.OnMessageRevised += OnKfChatMessage;
        KfClient.OnUsersParted += OnUsersParted;
        KfClient.OnUsersJoined += OnUsersJoined;
        KfClient.OnDisconnected += OnKfDisconnected;
        KfClient.OnConnected += OnKfConnected;
        KfClient.OnRoomJoined += OnRoomJoined;
        KfClient.OnRoomJoinFailed += OnFailedToJoinRoom;
        KfClient.OnRoomLeft += OnRoomLeft;
        KfClient.OnAdmissionFailed += OnAdmissionFailed;
        KfClient.OnSessionEstablished += OnSessionEstablished;
        KfClient.OnError += OnKfChatError;

        RoomUuid = settings[BuiltIn.Keys.KiwiFarmsRoomUuid].Value;
        if (RoomUuid == null)
        {
            _logger.Error($"{BuiltIn.Keys.KiwiFarmsRoomUuid} isn't set so the bot has no room to join. " +
                          "Once connected it'll log the rooms the chat host lists, set the right one and restart.");
        }
        else
        {
            // The client keeps this joined from here on, including after reconnects
            KfClient.JoinRoom(RoomUuid);
        }

        KfClient.StartAsync().Wait(_cancellationToken);

        _logger.Debug("Creating chat watchdog task");
        _kfChatWatchdog = KfWatchdogTask();
        _logger.Debug("Creating scheduled auto deletion task");
        _scheduledAutoDeleteTask = ScheduledDeletionTask();
        
        _logger.Debug("Trying to load homoglyphs");
        if (File.Exists("homoglyphs.csv"))
        {
            var sets = HomoglyphLoader.LoadSets("homoglyphs.csv");
            _homoglyphSearch = new HomoglyphSearch(sets);
        }
        
        _logger.Debug("Blocking the main thread");
        var exitEvent = new ManualResetEvent(false);
        exitEvent.WaitOne();
    }

    /// <summary>
    /// Ticket provider for the chat client. Gets called from its connection loop when a new chat session is needed
    /// </summary>
    private async Task<string> GetChatTicketAsync(CancellationToken ct)
    {
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.KiwiFarmsPassword]);
        var ticket = await _kfTokenService.GetChatTicketAsync(settings[BuiltIn.Keys.KiwiFarmsUsername].Value,
            settings[BuiltIn.Keys.KiwiFarmsPassword].Value, ct);
        // We may have logged in or solved a challenge along the way
        await _kfTokenService.SaveCookies();
        // The chat host sits behind the same KiwiFlare as the forum, so it needs any new clearance cookie as well
        KfClient.UpdateCookies(ForumCookiesForChat());
        return ticket;
    }

    /// <summary>
    /// Challenge solver for the chat client. Gets called from its connection loop when KiwiFlare answers with a 203
    /// </summary>
    private async Task<Dictionary<string, string>> SolveChatChallengeAsync(CancellationToken ct)
    {
        _logger.Info("Chat 203'd, solving the KiwiFlare challenge");
        await _kfTokenService.SolveChallengeAsync();
        await _kfTokenService.SaveCookies();
        return ForumCookiesForChat();
    }

    // Everything in the token service's jar apart from the chat session. That one belongs to the chat client, the
    // copy here is only for persisting it and might be an old one
    private Dictionary<string, string> ForumCookiesForChat()
    {
        return _kfTokenService.GetCookies().Where(cookie => cookie.Key != EphyraClient.SessionCookieName)
            .ToDictionary(cookie => cookie.Key, cookie => cookie.Value);
    }

    private void OnSessionEstablished(object sender)
    {
        // Hang onto the session cookie so a restart doesn't need a fresh ticket
        if (!KfClient.GetCookies().TryGetValue(EphyraClient.SessionCookieName, out var session)) return;
        _logger.Info("Got a new chat session, saving its cookie");
        _kfTokenService.SetCookie(EphyraClient.SessionCookieName, session);
        _kfTokenService.SaveCookies().Wait(_cancellationToken);
    }

    private void OnAdmissionFailed(object sender, ChatAdmissionFailedModel failure)
    {
        var failureLimit = SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsJoinFailLimit).Result.ToType<int>();
        _joinFailures++;
        _logger.Error($"Couldn't get a chat session, attempt {_joinFailures}. Failure was {failure.Failure}, status code {failure.StatusCode}");
        _logger.Error(failure.Exception);
        if (_joinFailures < failureLimit) return;
        _logger.Error("Seems we're in a loop. Wiping out cookies entirely in hopes it'll make this piece of shit work");
        _kfTokenService.WipeCookies();
        KfClient.ForgetSession();
        _joinFailures = 0;
    }

    private void OnFailedToJoinRoom(object sender, ChatRoomJoinFailedModel failure)
    {
        _logger.Error($"Couldn't join {failure.RoomUuid}. Chat returned: {failure.Code}");
        _logger.Error(failure.Terminal
            ? $"That's a flat out denial so the bot will not try again. Check {BuiltIn.Keys.KiwiFarmsRoomUuid} is right and the account is allowed in there."
            : "The chat client will try again shortly.");
    }

    private void OnRoomLeft(object sender, string roomUuid, string? reason)
    {
        _logger.Error($"Chat removed us from {roomUuid}, reason: {reason}");
    }

    private void OnRoomJoined(object sender, ChatRoomJoinedModel room)
    {
        if (room.RoomUuid != RoomUuid) return;
        _logger.Info($"Joined the room. Head sequence is {room.HeadSeq}, resumed from where we left off => {room.Resumed}");
        // Reset value to 0 as we've now successfully joined
        if (_joinFailures > 0) _joinFailures = 0;
        var replayLimit = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotDisconnectReplayLimit).Result.ToType<int>();
        // Send messages if there are any to replay (Assuming we DC'd and are now back)
        foreach (var replayMsg in SentMessages.Where(msg => msg.Status == SentMessageTrackerStatus.ChatDisconnected)
                     .TakeLast(replayLimit).ToList())
        {
            // Bypass the helpful method we have for sending messages so we don't create new sent message items for them
            // The validation of whether to send based on GambaSesh's presence etc. has already been performed for msgs here
            // Fresh nonce as the chat only honours them for ten minutes and who knows how long we were gone
            replayMsg.ClientNonce = EphyraClient.NewClientNonce();
            replayMsg.Status = SentMessageTrackerStatus.WaitingForResponse;
            replayMsg.SentAt = DateTimeOffset.UtcNow;
            replayMsg.SendAttempts = 1;
            if (!KfClient.SendMessageAsync(room.RoomUuid, replayMsg.Message, replayMsg.ClientNonce, _cancellationToken).Result)
            {
                replayMsg.Status = SentMessageTrackerStatus.Lost;
            }
        }
        foreach(var lostMsg in SentMessages.Where(msg => msg.Status == SentMessageTrackerStatus.ChatDisconnected).ToList())
        {
            lostMsg.Status = SentMessageTrackerStatus.Lost;
        }

        if (InitialStartCooldown) InitialStartCooldown = false;
    }

    private void OnKfChatError(object sender, ChatErrorModel error)
    {
        // For a message we tried to post, the chat refers to it by the nonce we sent it with
        var sentMessage = error.Ref == null ? null : SentMessages.LastOrDefault(msg => msg.ClientNonce == error.Ref);
        if (sentMessage == null)
        {
            _logger.Error($"Chat refused something: {error.Code} (ref: {error.Ref})");
            return;
        }

        if (error.IsTransient && sentMessage.SendAttempts < 4)
        {
            _logger.Info($"Chat couldn't take message {sentMessage.Reference} right now ({error.Code}), will try it again");
            _ = RetrySendAsync(sentMessage);
            return;
        }

        _logger.Error($"Chat refused message {sentMessage.Reference} with {error.Code}. Message was: {sentMessage.Message}");
        sentMessage.Status = SentMessageTrackerStatus.Lost;
    }

    private async Task RetrySendAsync(SentMessageTrackerModel message)
    {
        // Same nonce on purpose. If the first attempt did go through after all, the chat won't post it a second time
        await Task.Delay(TimeSpan.FromSeconds(message.SendAttempts), _cancellationToken);
        message.SendAttempts++;
        if (message.Status != SentMessageTrackerStatus.WaitingForResponse || message.ClientNonce == null || RoomUuid == null) return;
        if (!await KfClient.SendMessageAsync(RoomUuid, message.Message, message.ClientNonce, _cancellationToken))
        {
            message.Status = SentMessageTrackerStatus.Lost;
        }
    }

    private async Task KfWatchdogTask()
    {
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsPingInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            try
            {
                // The chat client pings and reconnects by itself. This is only here to give it a kick if it somehow
                // manages to sit there receiving nothing at all (pongs count) for longer than the inactivity timeout
                if (KfClient.IsHalted())
                {
                    _logger.Debug("Chat client has given up (banned or not permitted), leaving it to the dead bot detection");
                    continue;
                }
                var inactivityTime = DateTime.UtcNow - KfClient.LastPacketReceived;
                var lastReconnect = DateTime.UtcNow - _lastReconnectAttempt;

                _logger.Debug($"Last KF event was {inactivityTime:g} ago");
                var inactivityTimeout = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsInactivityTimeout)).ToType<int>();
                if (inactivityTime.TotalSeconds > inactivityTimeout && lastReconnect.TotalMinutes > 1)
                {
                    _logger.Error("Forcing reconnect as bot is completely dead");
                    await RecreateKfClientAsync();
                }
            }
            catch (Exception e)
            {
                // Nothing is watching this task, if it dies it takes the watchdog with it for good
                _logger.Error("Chat watchdog threw an exception, it'll try again next tick");
                _logger.Error(e);
            }
        }
    }

    /// <summary>
    /// Whether the bot is in working order right now: connected, sat in its room, hearing from the chat host and
    /// keeping up with what it's being sent
    /// </summary>
    private bool IsChatAlive()
    {
        // The dead bot detection starts before the client exists. A bot that's stuck starting up isn't alive either
        if (KfClient is not { } client) return false;
        if (client.IsHalted()) return false;
        // Being connected isn't enough. If the chat won't let us in the room (or threw us out of it) the pongs keep
        // coming and the bot is just as useless as if it were disconnected
        if (RoomUuid == null ? !client.IsConnected() : !client.IsJoined(RoomUuid)) return false;
        if (DateTime.UtcNow - client.LastPacketReceived > TimeSpan.FromMinutes(10)) return false;
        // A stuck event handler means no messages get through to the bot even though the connection is fine
        return client.EventBacklogAge < TimeSpan.FromMinutes(2);
    }
    
    private async Task KfDeadBotDetectionTask()
    {
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotDeadBotDetectionInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            try
            {
                if (IsChatAlive())
                {
                    _lastAlive = DateTime.UtcNow;
                    continue;
                }

                // This is counted from when the bot last actually worked, not from the last attempt at fixing it.
                // It used to go off the last reconnect attempt, which the watchdog makes every minute while the bot is
                // down, so the clock never ran out and the bot never got reaped
                var deadTime = DateTime.UtcNow - _lastAlive;
                // Read every time so it can be changed without restarting the bot
                var threshold = TimeSpan.FromSeconds(
                    (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotDeadBotThreshold)).ToType<int>());
                // Not been in working order for that long straight (15 minutes by default), and the chat client and
                // the watchdog have had plenty of goes at it by now. Either the site is completely dead or the bot
                // got screwed by a nasty error and can't recover
                if (deadTime <= threshold) continue;
                var shouldExit = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotExitOnDeath)).ToBoolean();
                _logger.Error("The bot as is dead beyond belief right now");
                _logger.Error($"IsConnected() -> {KfClient?.IsConnected()}");
                _logger.Error($"IsHalted() -> {KfClient?.IsHalted()}");
                _logger.Error($"In the room -> {(RoomUuid == null ? null : KfClient?.IsJoined(RoomUuid))}");
                _logger.Error($"inactivityTime -> {DateTime.UtcNow - KfClient?.LastPacketReceived:g}");
                _logger.Error($"Event backlog -> {KfClient?.EventBacklogAge:g}");
                _logger.Error($"deadTime -> {deadTime:g} (threshold is {threshold:g})");
                if (shouldExit) Environment.Exit(1);
                // Start the clock again so this goes off once per threshold instead of every tick
                _lastAlive = DateTime.UtcNow;
                if (KfClient == null)
                {
                    _logger.Error("Since we didn't exit and there's no chat client yet (startup is stuck), there's nothing to reconnect");
                    continue;
                }
                _logger.Error("Since we didn't exit, let's try forcing a reconnect");
                await RecreateKfClientAsync();
            }
            catch (Exception e)
            {
                // Nothing is watching this task, if it dies there's no dead bot detection for the rest of the run
                _logger.Error("Dead bot detection threw an exception, it'll try again next tick");
                _logger.Error(e);
            }
        }
    }

    private async Task ScheduledDeletionTask()
    {
        var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotScheduledDeletionInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        var failures = new Dictionary<string, int>();
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            if (!KfClient.IsConnected())
            {
                _logger.Debug("Not cleaning scheduled deletions up as we're disconnected");
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var removals = new List<ScheduledAutoDeleteModel>();
            // Copy as things get scheduled from all over the place while this is running
            foreach (var deletion in _scheduledDeletions.ToList())
            {
                if (deletion.DeleteAt > now) continue;
                if (deletion.Message.ChatMessageUuid == null)
                {
                    // Messages which were never going to be sent will never get an ID either
                    if (deletion.Message.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost)
                    {
                        removals.Add(deletion);
                        continue;
                    }
                    _logger.Error($"Can't clean up {deletion.Message.Reference} as it doesn't have a chat message ID");
                    if (GiveUpOnDeletion(failures, deletion.Message.Reference)) removals.Add(deletion);
                    continue;
                }

                try
                {
                    // Unlike Sneedchat this waits for the chat to say it's done, so we know if it needs another go
                    var result = await KfClient.DeleteMessageAsync(deletion.Message.ChatMessageUuid, ct: _cancellationToken);
                    if (result.Status is ChatCommandStatus.TimedOut or ChatCommandStatus.NotConnected)
                    {
                        _logger.Info($"Deletion of {deletion.Message.ChatMessageUuid} didn't go through ({result.Status}), leaving it in the queue");
                        if (GiveUpOnDeletion(failures, deletion.Message.Reference)) removals.Add(deletion);
                        continue;
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.Error($"Caught an exception while deleting {deletion.Message.ChatMessageUuid}");
                    _logger.Error(e);
                }
                removals.Add(deletion);
            }
            foreach (var removal in removals)
            {
                _scheduledDeletions.Remove(removal);
                failures.Remove(removal.Message.Reference);
            }
        }
    }

    private bool GiveUpOnDeletion(Dictionary<string, int> failures, string reference)
    {
        failures[reference] = failures.GetValueOrDefault(reference) + 1;
        if (failures[reference] <= 20) return false;
        _logger.Error($"Giving up on {reference} and removing it from the deletion queue");
        return true;
    }

    private async Task RefreshXfToken()
    {
        try
        {
            if (await _kfTokenService.IsLoggedIn())
            {
                _logger.Info("We were already logged in and should have a fresh cookie for chat now");
                _logger.Info("Updating cookies");
                await _kfTokenService.SaveCookies();
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (KfClient != null) KfClient.UpdateCookies(_kfTokenService.GetCookies());
                // Only seems to happen if the bot thinks it's already logged in
                return;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Caught an error when trying to retrieve a fresh cookie");
            _logger.Error(e);
            return;
        }
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.KiwiFarmsPassword]);
        try
        {
            await _kfTokenService.PerformLogin(settings[BuiltIn.Keys.KiwiFarmsUsername].Value!,
                settings[BuiltIn.Keys.KiwiFarmsPassword].Value!);
        }
        catch (Exception e)
        {
            _logger.Error("Caught an error when trying to login");
            _logger.Error(e);
            return;
        }

        _logger.Info("Successfully logged in");
        _logger.Info("Updating cookies");
        await _kfTokenService.SaveCookies();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (KfClient != null) KfClient.UpdateCookies(_kfTokenService.GetCookies());
    }
    
    /// <summary>
    /// Work out who a chat principal is in terms the rest of the bot understands (forum ID, row in the Users table).
    /// Ephyra only deals in principal UUIDs. The forum ID comes from the avatar URL when there is one and from a whois
    /// against the forum when there isn't, then the UUID is stored against the user so it only has to happen once
    /// </summary>
    /// <param name="principal">Principal as received from the chat</param>
    /// <param name="allowWhois">Whether it's OK to block on a whois lookup if the avatar doesn't give the ID away</param>
    /// <returns>The user. Id will be 0 if their forum ID couldn't be established</returns>
    private UserModel ResolveUser(ChatPrincipalModel principal, bool allowWhois)
    {
        var unresolved = new UserModel
        {
            Id = 0, Username = principal.DisplayName, AvatarUrl = principal.AvatarUrl, PrincipalUuid = principal.PrincipalUuid
        };
        if (string.IsNullOrEmpty(principal.PrincipalUuid)) return unresolved;
        lock (_userResolveLock)
        {
            if (_knownUsers.TryGetValue(principal.PrincipalUuid, out var known))
            {
                if (principal.AvatarUrl != null) known.AvatarUrl = principal.AvatarUrl;
                // Detect a username change
                if (principal.DisplayName.Length == 0 || known.Username == principal.DisplayName) return known;
                _logger.Debug("Username has updated, updating DB");
                known.Username = principal.DisplayName;
                using var renameDb = new ApplicationDbContext();
                var renamed = renameDb.Users.FirstOrDefault(u => u.PrincipalUuid == principal.PrincipalUuid);
                if (renamed == null) return known;
                renamed.KfUsername = principal.DisplayName;
                renameDb.SaveChanges();
                return known;
            }

            using var db = new ApplicationDbContext();
            var userDb = db.Users.FirstOrDefault(u => u.PrincipalUuid == principal.PrincipalUuid);
            if (userDb == null)
            {
                var forumId = principal.ForumUserIdFromAvatar;
                string? forumUsername = null;
                if (forumId == null && allowWhois)
                {
                    var whois = WhoisPrincipal(principal.PrincipalUuid);
                    forumId = whois?.UserId;
                    forumUsername = whois?.Username;
                }

                if (forumId == null) return unresolved;
                var username = principal.DisplayName.Length > 0 ? principal.DisplayName : forumUsername ?? $"Unknown {forumId}";
                userDb = db.Users.FirstOrDefault(u => u.KfId == forumId);
                if (userDb == null)
                {
                    _logger.Debug("Adding user to DB");
                    userDb = new UserDbModel { KfId = forumId.Value, KfUsername = username, PrincipalUuid = principal.PrincipalUuid };
                    db.Users.Add(userDb);
                }
                else
                {
                    if (userDb.PrincipalUuid != null)
                    {
                        _logger.Error($"Forum user {forumId} was linked to principal {userDb.PrincipalUuid} but has turned up as {principal.PrincipalUuid}. Relinking.");
                    }
                    _logger.Info($"Linking existing user {userDb.KfUsername} ({userDb.KfId}) to principal {principal.PrincipalUuid}");
                    userDb.PrincipalUuid = principal.PrincipalUuid;
                }
            }

            if (principal.DisplayName.Length > 0 && userDb.KfUsername != principal.DisplayName)
            {
                _logger.Debug("Username has updated, updating DB");
                userDb.KfUsername = principal.DisplayName;
            }

            db.SaveChanges();
            var user = new UserModel
            {
                Id = userDb.KfId, Username = userDb.KfUsername, AvatarUrl = principal.AvatarUrl, PrincipalUuid = principal.PrincipalUuid
            };
            _knownUsers[principal.PrincipalUuid] = user;
            return user;
        }
    }

    // Caller must hold _userResolveLock
    private ChatWhoisModel? WhoisPrincipal(string principalUuid)
    {
        if (_whoisFailures.TryGetValue(principalUuid, out var failedAt) && DateTime.UtcNow - failedAt < TimeSpan.FromMinutes(5))
        {
            return null;
        }

        try
        {
            var whois = _kfTokenService.WhoisAsync(principalUuid, _cancellationToken).Result;
            if (whois == null)
            {
                _logger.Error($"Forum has no idea who principal {principalUuid} is");
                _whoisFailures[principalUuid] = DateTime.UtcNow;
                return null;
            }

            _logger.Info($"Whois says principal {principalUuid} is {whois.Username} ({whois.UserId})");
            _whoisFailures.Remove(principalUuid);
            return whois;
        }
        catch (Exception e)
        {
            _logger.Error($"Whois lookup for principal {principalUuid} failed");
            _logger.Error(e);
            _whoisFailures[principalUuid] = DateTime.UtcNow;
            return null;
        }
    }

    private void OnKfChatMessage(object sender, ChatMessageModel chatMessage)
    {
        // Side rooms and anything else we didn't ask for
        if (chatMessage.RoomUuid != RoomUuid) return;
        var settings = SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.GambaSeshDetectEnabled,
                BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.BotRespondToDiscordImpersonation])
            .Result;
        // The backlog the chat sends when joining a room from scratch. Stuff we've either dealt with already or weren't
        // around for, so it's noted but not acted on. (Things missed during a disconnect are NOT history, the chat
        // replays those from where we left off and they're handled as if they'd just happened)
        var isHistory = chatMessage.Origin == ChatEntryOrigin.History;
        var isOwnMessage = chatMessage.Author.PrincipalUuid == KfClient.PrincipalUuid;
        var author = ResolveUser(chatMessage.Author, allowWhois: !isHistory);
        // Commands were written for Sneedchat where a mention is just text, so give them @Name instead of [user=uuid]Name[/user]
        var messageRaw = EphyraBbCode.MentionsToPlainText(chatMessage.BodyBbcode);
        var message = new BotCommandMessageModel
        {
            Author = author,
            MessageRaw = messageRaw,
            Message = chatMessage.BodyHtml,
            MessageDate = chatMessage.PostedAt,
            MessageEditDate = chatMessage.EditedAt,
            MessageRawHtmlDecoded = messageRaw,
            MessageUuid = chatMessage.MessageUuid,
            RoomUuid = chatMessage.RoomUuid,
            MessageBbCode = chatMessage.BodyBbcode,
            MentionedPrincipalUuids = chatMessage.Mentions.Select(mention => mention.PrincipalUuid).ToList()
        };

        if (message.MessageEditDate == null)
        {
            _logger.Info($"KF ({message.MessageDate.ToLocalTime():HH:mm:ss}) <{message.Author.Username}> {message.MessageRaw}");
        }
        // Update last edit timestamp
        if (isOwnMessage && message.MessageEditDate != null)
        {
            var sentMessage = SentMessages.FirstOrDefault(x => x.ChatMessageUuid == message.MessageUuid);
            if (sentMessage != null)
            {
                sentMessage.LastEdited = message.MessageEditDate.Value;
            }
        }
        if (isOwnMessage && message.MessageEditDate == null && !isHistory)
        {
            // The chat echoes the nonce we sent the message with, so there's no guesswork about which one this is
            var sentMessage = chatMessage.ClientNonce == null
                ? null
                : SentMessages.LastOrDefault(sent => sent.ClientNonce == chatMessage.ClientNonce);
            // Unless the echo got lost to a disconnect. What comes back on the replay has no nonce, so match on content
            sentMessage ??= SentMessages.FirstOrDefault(sent =>
                sent.Message == chatMessage.BodyBbcode && sent is { Status: SentMessageTrackerStatus.WaitingForResponse, Type: SentMessageType.ChatMessage });
            if (sentMessage == null && chatMessage.Origin == ChatEntryOrigin.Recovered)
            {
                _logger.Error("Received message from chat that I sent but have no idea about. Message Data Follows:");
                _logger.Error(JsonSerializer.Serialize(message));
                _logger.Error("Last item inserted into the sent messages collection waiting for response:");
                var latest =
                    SentMessages.LastOrDefault(msg => msg is { Status: SentMessageTrackerStatus.WaitingForResponse, Type: SentMessageType.ChatMessage });
                _logger.Error(JsonSerializer.Serialize(latest));
                if (latest != null)
                {
                    // Only done for messages replayed after a disconnect, which is the one time an echo can go
                    // missing. Accounts for the occasional mismatch due to messages not being 1:1 with what we
                    // thought we sent
                    _logger.Info("Just going to lazily associate it with the latest message");
                    sentMessage = latest;
                }
            }

            if (sentMessage == null)
            {
                // Somebody is logged into the bot's account elsewhere and posting, or it's from before a restart
                _logger.Debug($"Message {message.MessageUuid} is from our account but isn't one this instance sent");
            }
            else
            {
                sentMessage.ChatMessageUuid = message.MessageUuid;
                sentMessage.Delay = DateTimeOffset.UtcNow - sentMessage.SentAt;
                sentMessage.Status = SentMessageTrackerStatus.ResponseReceived;
            }
        }

        var gambaSeshUserId = settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>();
        if (message.Author.Id == gambaSeshUserId && BotServices.TemporarilyBypassGambaSeshForDiscord &&
            message.MessageRaw.Contains("discord16"))
        {
            _logger.Info("GambaSesh fixed itself, turning off bypass");
            BotServices.TemporarilyBypassGambaSeshForDiscord = false;
        }
        if (settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean() && !isHistory && message.Author.Id == gambaSeshUserId && !GambaSeshPresent)
        {
            _logger.Info("Received a GambaSesh message while thinking he's not here. Setting the presence flag to avoid spamming chat");
            GambaSeshPresent = true;
        }

        // Basically the bot will ignore the message if it has been seen before and its edit time is the same
        // The chat client already makes sure nothing is delivered twice, this is a second line of defence for the
        // case where the chat makes us resync a room from scratch
        if (!_seenMessages.Any(msg =>
                msg.MessageUuid == message.MessageUuid && msg.LastEdited == message.MessageEditDate) &&
            !isHistory)
        {
            _logger.Debug("Passing message to command interface");
            try
            {
                _botCommands.ProcessMessage(message);
            }
            catch (Exception e)
            {
                _logger.Error("ProcessMessage threw an exception");
                _logger.Error(e);
            }
        }

        // Update or add the element to keep it in sync
        var existingMsg = _seenMessages.FirstOrDefault(msg => msg.MessageUuid == message.MessageUuid);
        if (existingMsg != null)
        {
            existingMsg.LastEdited = message.MessageEditDate;
        }
        else
        {
            _seenMessages.Add(new SeenMessageMetadataModel {MessageUuid = chatMessage.MessageUuid, LastEdited = message.MessageEditDate});
        }

        // Everything from here on is about things happening right now
        if (isHistory) return;
        if (message.Author.Id != 0)
        {
            UpdateUserLastActivityAsync(message.Author.Id, WhoWasActivityType.Message).Wait(_cancellationToken);
        }
        // Strip weird control characters and just allow basic punctuation + whitespace
        var kindaSanitized = new string(chatMessage.BodyBbcode
            .Where(c => c == ' ' || char.IsPunctuation(c) || char.IsLetter(c) || char.IsDigit(c)).ToArray());
        var homoglyphFound = false;
        if (_homoglyphSearch != null)
        {
            var searchStrings =
                SettingsProvider.GetValueAsync(BuiltIn.Keys.BotDiscordImpersonationSearchStrings).Result
                .JsonDeserialize<List<string>>();
            var lowerStrings = searchStrings?.Select(x => x.ToLower()).ToList();
            var search = _homoglyphSearch.Search(kindaSanitized.ToLower(), lowerStrings);
            if (search.Count == 0)
            {
                search = _homoglyphSearch.Search(kindaSanitized, searchStrings);
            }
            homoglyphFound = search.Count > 0;
        }
        if ((message.MessageEditDate == null || message.MessageDate > DateTimeOffset.UtcNow.AddSeconds(-15))
            && message.Author.Id != gambaSeshUserId &&
            !isOwnMessage &&
            settings[BuiltIn.Keys.BotRespondToDiscordImpersonation].ToBoolean() && kindaSanitized.Contains("[img]") 
            && homoglyphFound)
        {
            var deleteOrNah = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotDiscordImpersonationDeleteAttempt)
                .Result.ToBoolean();
            if (deleteOrNah)
            {
                _ = KfClient.DeleteMessageAsync(chatMessage.MessageUuid);
            }
            else
            {
                SendChatMessage($"☝️ {message.Author.Username} is a nigger faggot", true);
            }
        }
    }

    // Reference for Sneedchat hardcoded length limit
    // https://github.com/jaw-sh/ruforo/blob/master/src/web/chat/connection.rs#L226
    /// <summary>
    /// Async method for sending a chat message
    /// </summary>
    /// <param name="message">The message you wish to send</param>
    /// <param name="bypassSeshDetect">Whether to bypass detecting if GambaSesh is present and send unconditionally</param>
    /// <param name="lengthLimitBehavior">What behavior to use when encountering a message that exceeds the length limit</param>
    /// <param name="lengthLimit">Length limit to enforce in bytes</param>
    /// <param name="autoDeleteAfter">Length of time until the message is auto deleted, null to disable. Starts counting from when the message is echoed by Sneedchat</param>
    /// <returns>An object you can use to check the status of the message and get its ID for editing/deleting later</returns>
    public async Task<SentMessageTrackerModel> SendChatMessageAsync(string message, bool bypassSeshDetect = false, LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.TruncateNicely, int lengthLimit = 2048, TimeSpan? autoDeleteAfter = null)
    {
        var settings = await SettingsProvider
            .GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsSuppressChatMessages, BuiltIn.Keys.GambaSeshDetectEnabled
            ]);
        var reference = Guid.NewGuid().ToString();
        var messageTracker = new SentMessageTrackerModel
        {
            Reference = reference,
            Message = message.TrimEnd(), // Sneedchat trims trailing spaces
            Status = SentMessageTrackerStatus.Unknown,
            Type = SentMessageType.ChatMessage
        };
        if (settings[BuiltIn.Keys.KiwiFarmsSuppressChatMessages].ToBoolean())
        {
            _logger.Info("Not sending message as SuppressChatMessages is enabled");
            _logger.Info($"Message was: {message}");
            messageTracker.Status = SentMessageTrackerStatus.NotSending;
            SentMessages.Add(messageTracker);
            return messageTracker;
        }
        if (GambaSeshPresent && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean() && !bypassSeshDetect)
        {
            _logger.Info($"Not sending message '{message}' as GambaSesh is present");
            messageTracker.Status = SentMessageTrackerStatus.NotSending;
            SentMessages.Add(messageTracker);
            return messageTracker;
        }

        if (RoomUuid == null)
        {
            _logger.Info($"Not sending message '{message}' as {BuiltIn.Keys.KiwiFarmsRoomUuid} isn't set");
            messageTracker.Status = SentMessageTrackerStatus.NotSending;
            SentMessages.Add(messageTracker);
            return messageTracker;
        }

        // Has to be in the room, not just connected, or the chat would never echo the message back to us
        if (!KfClient.IsJoined(RoomUuid))
        {
            _logger.Info($"Not sending message '{message}' as chat is not connected");
            messageTracker.Status = SentMessageTrackerStatus.ChatDisconnected;
            SentMessages.Add(messageTracker);
            return messageTracker;
        }
        
        if (messageTracker.Message.Utf8LengthBytes() > lengthLimit && lengthLimitBehavior != LengthLimitBehavior.DoNothing)
        {
            if (lengthLimitBehavior == LengthLimitBehavior.RefuseToSend)
            {
                _logger.Info("Refusing to send message as it exceeds the length limit and LengthLimitBehavior is RefuseToSend");
                messageTracker.Status = SentMessageTrackerStatus.NotSending;
                SentMessages.Add(messageTracker);
                return messageTracker;
            }
            if (lengthLimitBehavior == LengthLimitBehavior.TruncateNicely)
            {
                // '…' is 3 bytes so we have to make room for it
                messageTracker.Message = messageTracker.Message.TruncateBytes(lengthLimit - 3).TrimEnd() + "…";
            }

            if (lengthLimitBehavior == LengthLimitBehavior.TruncateExactly)
            {
                // TrimEnd in case you end up truncating on a space (happened during testing) as Sneedchat will trim it
                messageTracker.Message = messageTracker.Message.TruncateBytes(lengthLimit).TrimEnd();
            }
        }
        
        messageTracker.Status = SentMessageTrackerStatus.WaitingForResponse;
        messageTracker.SentAt = DateTimeOffset.UtcNow;
        // The chat echoes this back with the message, which is how OnKfChatMessage ties the two together
        messageTracker.ClientNonce = EphyraClient.NewClientNonce();
        messageTracker.SendAttempts = 1;
        _logger.Debug($"Message is {messageTracker.Message.Utf8LengthBytes()} bytes");
        SentMessages.Add(messageTracker);
        if (!await KfClient.SendMessageAsync(RoomUuid, messageTracker.Message, messageTracker.ClientNonce, _cancellationToken))
        {
            // Lost the connection in the last few milliseconds (it'll get replayed) or the chat will never take it
            // (frozen, or bigger than a frame is allowed to be)
            messageTracker.Status = KfClient.IsJoined(RoomUuid)
                ? SentMessageTrackerStatus.Lost
                : SentMessageTrackerStatus.ChatDisconnected;
            return messageTracker;
        }
        if (autoDeleteAfter != null)
        {
            ScheduleMessageAutoDelete(messageTracker, autoDeleteAfter.Value);
        }
        return messageTracker;
    }
    
    /// <summary>
    /// Used to send a whisper. Ephyra doesn't have them so this sends nothing at all, the message is just logged.
    /// (The closest thing is a "side room" which is a different beast: a private room you open with someone)
    /// </summary>
    /// <param name="recipient">Kiwi Farms user ID of the recipient for this whisper</param>
    /// <param name="message">The message you wish to whisper</param>
    /// <param name="lengthLimitBehavior">Ignored</param>
    /// <param name="lengthLimit">Ignored</param>
    /// <returns>A tracker which is always NotSending</returns>
    [Obsolete(WhispersGoneMessage)]
    public Task<SentMessageTrackerModel> SendWhisperAsync(int recipient, string message, LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.TruncateNicely, int lengthLimit = 2048)
    {
        _logger.Error($"Something tried to whisper {recipient} but whispers don't exist anymore. Message was: {message}");
        var messageTracker = new SentMessageTrackerModel
        {
            Reference = Guid.NewGuid().ToString(),
            Message = message.TrimEnd(),
            Status = SentMessageTrackerStatus.NotSending,
            Type = SentMessageType.Whisper,
            WhisperMessage = message.TrimEnd()
        };
        SentMessages.Add(messageTracker);
        return Task.FromResult(messageTracker);
    }

    /// <summary>
    /// Exposes the private task used to delete messages based on a TimeSpan in case you want to use it on-demand
    /// e.g. for cleaning up a gambling message only after the game has finished
    /// </summary>
    /// <param name="message">The message you want to delete</param>
    /// <param name="deleteAfter">When you want it deleted</param>
    public void ScheduleMessageAutoDelete(SentMessageTrackerModel message, TimeSpan deleteAfter)
    {
        _scheduledDeletions.Add(new ScheduledAutoDeleteModel
        {
            Message = message,
            DeleteAt = DateTimeOffset.UtcNow.Add(deleteAfter)
        });
    }
    
    /// <summary>
    /// Exposes the private task used to delete messages based on a TimeSpan in case you want to use it on-demand
    /// e.g. for cleaning up a gambling message only after the game has finished
    /// </summary>
    /// <param name="messageUuid">The message you want to delete where you only have a message UUID
    /// NOTE: The bot doesn't check against its sent message tracker, so you can use this with messages
    /// the bot was not responsible for sending or were lost due to a restart.</param>
    /// <param name="deleteAfter">When you want it deleted</param>
    public void ScheduleMessageAutoDelete(string messageUuid, TimeSpan deleteAfter)
    {
        _scheduledDeletions.Add(new ScheduledAutoDeleteModel
        {
            Message = new SentMessageTrackerModel
            {
                ChatMessageUuid = messageUuid,
                Delay = TimeSpan.Zero,
                LastEdited = DateTimeOffset.UtcNow,
                Message = "placeholder because I'm nigger rigging this shit big time",
                Reference = Guid.NewGuid().ToString(),
                SentAt = DateTimeOffset.UtcNow,
                Status = SentMessageTrackerStatus.ResponseReceived,
                Type = SentMessageType.ChatMessage
            },
            DeleteAt = DateTimeOffset.UtcNow.Add(deleteAfter)
        });
    }

    /// <summary>
    /// Non-async method which wraps the async method for sending a chat message
    /// </summary>
    /// <param name="message">The message you wish to send</param>
    /// <param name="bypassSeshDetect">Whether to bypass detecting if GambaSesh is present and send unconditionally</param>
    /// <param name="lengthLimitBehavior">What behavior to use when encountering a message that exceeds the length limit</param>
    /// <param name="lengthLimit">Length limit to enforce in bytes</param>
    /// <param name="autoDeleteAfter">Length of time until the message is auto deleted, null to disable. Starts counting from when the message is echoed by Sneedchat</param>
    /// <returns>An object you can use to check the status of the message and get its ID for editing/deleting later</returns>
    public SentMessageTrackerModel SendChatMessage(string message, bool bypassSeshDetect = false,
        LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.TruncateNicely, int lengthLimit = 2048, TimeSpan? autoDeleteAfter = null)
    {
        return SendChatMessageAsync(message, bypassSeshDetect, lengthLimitBehavior, lengthLimit, autoDeleteAfter).Result;
    }

    public async Task<List<SentMessageTrackerModel>> SendChatMessagesAsync(List<string> messages,
        bool bypassSeshDetect = false, LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.RefuseToSend, TimeSpan? autoDeleteAfter = null)
    {
        List<SentMessageTrackerModel> sentMessages = [];

        foreach (var message in messages)
        {
            sentMessages.Add(await SendChatMessageAsync(message, bypassSeshDetect, lengthLimitBehavior, autoDeleteAfter: autoDeleteAfter));
            // Delay sending each message, hopefully this will help the issue where messages come out of order
            await Task.Delay(TimeSpan.FromMilliseconds(100), _cancellationToken);
        }

        return sentMessages;
    }

    public SentMessageTrackerModel GetSentMessageStatus(string reference)
    {
        var message = SentMessages.FirstOrDefault(m => m.Reference == reference);
        if (message == null)
        {
            throw new SentMessageNotFoundException();
        }

        return message;
    }

    /// <summary>
    /// Wait for a chat message to be successfully delivered or not
    /// </summary>
    /// <param name="message">Reference to the message you're waiting for</param>
    /// <param name="patience">How long to wait</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the message was echoed, false otherwise</returns>
    public async Task<bool> WaitForChatMessageAsync(SentMessageTrackerModel message, TimeSpan? patience = null, CancellationToken ct = default)
    {
        if (patience == null)
        {
            patience = TimeSpan.FromSeconds(60);
        }

        var patienceEnds = DateTimeOffset.UtcNow.Add(patience.Value);
        while (message.ChatMessageUuid == null)
        {
            if (DateTimeOffset.UtcNow > patienceEnds) return false;
            if (message.Status is SentMessageTrackerStatus.Lost or SentMessageTrackerStatus.NotSending) return false;
            await Task.Delay(100, ct);
        }

        return true;
    }

    public class SentMessageNotFoundException : Exception;

    private void OnUsersJoined(object sender, string roomUuid, List<ChatPrincipalModel> principals)
    {
        if (roomUuid != RoomUuid) return;
        _logger.Debug($"Received {principals.Count} user join events");
        var needWhois = new List<ChatPrincipalModel>();
        foreach (var principal in principals)
        {
            // No whois here. On joining a room this is everybody in it, and there'd be a lookup for every single
            // person without an avatar holding up everything else the chat is trying to tell us
            var user = ResolveUser(principal, allowWhois: false);
            if (user.Id == 0)
            {
                needWhois.Add(principal);
                continue;
            }
            HandleUserJoined(user);
        }

        if (needWhois.Count == 0) return;
        _logger.Info($"{needWhois.Count} user(s) joined who can't be identified from their avatar, asking the forum who they are");
        _ = Task.Run(() => ResolveJoinedUsersAsync(needWhois), _cancellationToken);
    }

    private async Task ResolveJoinedUsersAsync(List<ChatPrincipalModel> principals)
    {
        foreach (var principal in principals)
        {
            try
            {
                var user = ResolveUser(principal, allowWhois: true);
                if (user.Id == 0)
                {
                    _logger.Error($"{principal.DisplayName} ({principal.PrincipalUuid}) joined but their forum ID couldn't be found, so the bot will ignore them for now");
                    continue;
                }

                // They may well have left again by the time the forum got back to us
                if (RoomUuid == null || KfClient.GetPresence(RoomUuid).All(present => present.PrincipalUuid != principal.PrincipalUuid)) continue;
                HandleUserJoined(user);
                // Be gentle with the forum
                await Task.Delay(TimeSpan.FromMilliseconds(250), _cancellationToken);
            }
            catch (Exception e)
            {
                _logger.Error($"Caught an exception while resolving {principal.PrincipalUuid}");
                _logger.Error(e);
            }
        }
    }

    private void HandleUserJoined(UserModel user)
    {
        var settings = SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled, BuiltIn.Keys.BotKeesSeen])
            .Result;
        lock (_currentUsersLock)
        {
            _currentUsersInChat.RemoveAll(u => u.PrincipalUuid == user.PrincipalUuid);
            _currentUsersInChat.Add(user);
        }

        if (user.Id == settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>() && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean())
        {
            _logger.Info("GambaSesh is now present");
            GambaSeshPresent = true;
        }

        if (user.Id == 89776 && !settings[BuiltIn.Keys.BotKeesSeen].ToBoolean())
        {
            _logger.Info("Kees has joined!");
            SendChatMessage($":!: :!: {user.Username} has appeared! :!: :!:", true);
            SettingsProvider.SetValueAsBooleanAsync(BuiltIn.Keys.BotKeesSeen, true).Wait(_cancellationToken);
        }
        _logger.Info($"{user.Username} joined!");
        // Adding them to the DB (or noticing a username change) was already taken care of by ResolveUser
        UpdateUserLastActivityAsync(user.Id, WhoWasActivityType.Join).Wait(_cancellationToken);
    }

    private void OnUsersParted(object sender, string roomUuid, List<ChatPrincipalModel> principals)
    {
        if (roomUuid != RoomUuid) return;
        var userIds = new List<int>();
        lock (_userResolveLock)
        {
            foreach (var principal in principals)
            {
                // Anyone not in here never had their join processed either
                if (_knownUsers.TryGetValue(principal.PrincipalUuid, out var user)) userIds.Add(user.Id);
            }
        }

        lock (_currentUsersLock)
        {
            _currentUsersInChat.RemoveAll(u => principals.Any(principal => principal.PrincipalUuid == u.PrincipalUuid));
        }
        var settings = SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled])
            .Result;
        if (userIds.Contains(settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>()) && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean())
        {
            _logger.Info("GambaSesh is no longer present");
            GambaSeshPresent = false;
        }

        foreach (var user in userIds)
        {
            UpdateUserLastActivityAsync(user, WhoWasActivityType.Part).Wait(_cancellationToken);
        }
    }

    private async Task UpdateUserLastActivityAsync(int kfId, WhoWasActivityType type)
    {
        await using var db = new ApplicationDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.KfId == kfId, _cancellationToken);
        if (user == null)
        {
            _logger.Error($"Failed to find user with KfId = {kfId} for the purposes of updating their last activity");
            return;
        }

        var activity =
            await db.UsersWhoWere.FirstOrDefaultAsync(u => u.User == user && u.ActivityType == type, _cancellationToken);
        if (activity == null)
        {
            await db.UsersWhoWere.AddAsync(new UserWhoWasDbModel
            {
                User = user,
                FirstOccurence = DateTimeOffset.UtcNow,
                ActivityType = type,
                LatestOccurence = DateTimeOffset.UtcNow
            }, _cancellationToken);
            await db.SaveChangesAsync(_cancellationToken);
            return;
        }
        activity.LatestOccurence = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(_cancellationToken);
    }

    /// <summary>
    /// Kick the chat client into dropping whatever connection it has and connecting again right now. The name is a
    /// leftover from Sneedchat where the whole client had to be thrown away, this one reconnects on its own and only
    /// needs this if it's been declared dead or has given up
    /// </summary>
    private async Task RecreateKfClientAsync()
    {
        _lastReconnectAttempt = DateTime.UtcNow;
        _logger.Info("Forcing the chat client to reconnect");
        await KfClient.ReconnectAsync();
    }

    private void OnKfDisconnected(object sender, ChatDisconnectedModel disconnection)
    {
        _logger.Error($"Chat disconnected (or never connected). Was Connected => {disconnection.WasConnected}; Was Welcomed => {disconnection.WasWelcomed}");
        _logger.Error($"Close Code => {disconnection.CloseCode}; Close Description => {disconnection.CloseDescription}; Upgrade Status Code => {disconnection.UpgradeStatusCode}");
        _logger.Error(disconnection.Exception);
        lock (_currentUsersLock)
        {
            _currentUsersInChat.Clear();
        }
        _logger.Info("Resetting GambaSesh presence so it can resync if he crashed while the bot was DC'd");
        GambaSeshPresent = false;
        if (disconnection.WillReconnect)
        {
            _logger.Info("The chat client will reconnect by itself");
            return;
        }

        _logger.Error($"The chat client has given up: {disconnection.HaltReason}. It won't reconnect until the dead bot detection forces it to.");
    }
    
    private void OnKfConnected(object sender, ChatWelcomeModel welcome)
    {
        _lastReconnectAttempt = DateTime.UtcNow;
        _logger.Info($"Chat connected as {welcome.DisplayName} ({welcome.PrincipalUuid}). Link state is {welcome.LinkState}");
        if (RoomUuid != null) return;
        // Nothing configured, so at least make it easy to find out what to configure
        try
        {
            var rooms = KfClient.GetRoomHeadsAsync(ct: _cancellationToken).Result;
            _logger.Error($"{BuiltIn.Keys.KiwiFarmsRoomUuid} isn't set. The chat lists {rooms.Count} room(s):");
            foreach (var room in rooms)
            {
                _logger.Error($"  {room.RoomUuid} => {room.RawJson}");
            }
        }
        catch (Exception e)
        {
            _logger.Error("Couldn't get the list of rooms from the chat");
            _logger.Error(e);
        }
    }

    public UserModel? FindUserByName(string username)
    {
        lock (_currentUsersLock)
        {
            return _currentUsersInChat.FirstOrDefault(u => u.Username.Equals(username, StringComparison.CurrentCulture));
        }
    }

    public enum LengthLimitBehavior
    {
        // Append …
        TruncateNicely,
        // Truncate regardless of whether it's mid-word and don't add a ...
        TruncateExactly,
        // Set status to NotSending
        RefuseToSend,
        // Try to send the message anyway, even though Sneedchat will just silently eat it
        DoNothing
    }

    private class ScheduledAutoDeleteModel
    {
        public required SentMessageTrackerModel Message { get; set; }
        public required DateTimeOffset DeleteAt { get; set; }
    }
    
    /// <summary>
    /// Thin wrapper to decide whether to whisper or chat message respond
    /// </summary>
    /// <param name="origMsg">The original message you received (so I know if it was a whisper)</param>
    /// <param name="response">Message you want to send</param>
    /// <param name="bypassGambaSesh">Whether to bypass gambasesh (not applicable for whispers)</param>
    /// <param name="autoDeleteAfter">Whether to auto delete after a period of time (not applicable to whispers)</param>
    /// <param name="lengthLimitBehavior">What behavior to use for messages which exceed the length limit</param>
    /// <returns></returns>
    public async Task<SentMessageTrackerModel> ReplyToUser(BotCommandMessageModel origMsg, string response,
        bool bypassGambaSesh = false, TimeSpan? autoDeleteAfter = null, LengthLimitBehavior lengthLimitBehavior = ChatBot.LengthLimitBehavior.TruncateNicely)
    {
        if (origMsg.IsWhisper)
        {
            return await SendWhisperAsync(origMsg.Author.Id, response,
                lengthLimitBehavior: lengthLimitBehavior);
        }

        return await SendChatMessageAsync(response, bypassGambaSesh, lengthLimitBehavior,
            autoDeleteAfter: autoDeleteAfter);
    }
}