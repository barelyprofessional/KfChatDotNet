using System.Net;
using System.Text;
using System.Text.Json;
using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IO;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot;

public class ChatBot
{
    internal readonly ChatClient KfClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private readonly List<SeenMessageMetadataModel> _seenMessages = [];
    // Suppresses the command handler on initial start, so it doesn't pick up things already handled on restart
    internal bool InitialStartCooldown = true;
    private readonly CancellationToken _cancellationToken = new();
    private readonly BotCommands _botCommands;
    public readonly List<SentMessageTrackerModel> SentMessages = [];
    internal bool GambaSeshPresent;
    internal readonly BotServices BotServices;
    private Task _kfChatPing;
    private KfTokenService _kfTokenService;
    private int _joinFailures = 0;
    
    public ChatBot()
    {
        _logger.Info("Bot starting!");

        var settings = Helpers.GetMultipleValues([
            BuiltIn.Keys.KiwiFarmsWsEndpoint, BuiltIn.Keys.KiwiFarmsDomain,
            BuiltIn.Keys.Proxy, BuiltIn.Keys.KiwiFarmsWsReconnectTimeout]).Result;

        _kfTokenService = new KfTokenService(settings[BuiltIn.Keys.KiwiFarmsDomain].Value!,
            settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        
        KfClient = new ChatClient(new ChatClientConfigModel
        {
            WsUri = new Uri(settings[BuiltIn.Keys.KiwiFarmsWsEndpoint].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsWsEndpoint} cannot be null")),
            XfSessionToken = _kfTokenService.GetXfSessionCookie(),
            CookieDomain = settings[BuiltIn.Keys.KiwiFarmsDomain].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsDomain} cannot be null"),
            Proxy = settings[BuiltIn.Keys.Proxy].Value,
            ReconnectTimeout = settings[BuiltIn.Keys.KiwiFarmsWsReconnectTimeout].ToType<int>()
        });
        
        if (_kfTokenService.GetXfSessionCookie() == null)
        {
            RefreshXfToken().Wait(_cancellationToken);
        }
  
        _logger.Debug("Creating bot command instance");
        _botCommands = new BotCommands(this, _cancellationToken);

        KfClient.OnMessages += OnKfChatMessage;
        KfClient.OnUsersParted += OnUsersParted;
        KfClient.OnUsersJoined += OnUsersJoined;
        KfClient.OnWsDisconnection += OnKfWsDisconnected;
        KfClient.OnWsReconnect += OnKfWsReconnected;
        KfClient.OnFailedToJoinRoom += OnFailedToJoinRoom;
        
        KfClient.StartWsClient().Wait(_cancellationToken);

        _logger.Debug("Creating ping task");
        _kfChatPing = KfPingTask();
        
        _logger.Debug("Starting services");
        BotServices = new BotServices(this, _cancellationToken);
        BotServices.InitializeServices();
        
        _logger.Debug("Blocking the main thread");
        var exitEvent = new ManualResetEvent(false);
        exitEvent.WaitOne();
    }

    private void OnFailedToJoinRoom(object sender, string message)
    {
        var failureLimit = Helpers.GetValue(BuiltIn.Keys.KiwiFarmsJoinFailLimit).Result.ToType<int>();
        _joinFailures++;
        _logger.Error($"Couldn't join the room, attempt {_joinFailures}. KF returned: {message}");
        _logger.Error("This is likely due to the session cookie expiring. Retrieving a new one.");
        if (_joinFailures >= failureLimit)
        {
            _logger.Error("Seems we're in a rejoin loop. Wiping out cookies entirely in hopes it'll make this piece of shit work");
            _kfTokenService.WipeCookies();
        }
        RefreshXfToken().Wait(_cancellationToken);
        _kfTokenService.SaveCookies().Wait(_cancellationToken);
        // Shouldn't be null if we've just refreshed the token
        // It's only null if a logon has never been attempted since the cookie DB entry was created
        KfClient.UpdateToken(_kfTokenService.GetXfSessionCookie()!);
        _logger.Info("Retrieved fresh token. Reconnecting.");
        KfClient.Disconnect();
        KfClient.StartWsClient().Wait(_cancellationToken);
        _logger.Info("Client should be reconnecting now");
    }

    private async Task KfPingTask()
    {
        var interval = (await Helpers.GetValue(BuiltIn.Keys.KiwiFarmsPingInterval)).ToType<int>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            _logger.Debug("Pinging KF");
            if (KfClient.IsConnected())
            {
                KfClient.SendMessage("/ping");
            }
            else
            {
                _logger.Info("Not pinging the connection as we're currently disconnected");
            }
            var inactivityTime = DateTime.UtcNow - KfClient.LastPacketReceived;
            _logger.Debug($"Last KF event was {inactivityTime:g} ago");
            var inactivityTimeout = (await Helpers.GetValue(BuiltIn.Keys.KiwiFarmsInactivityTimeout)).ToType<int>();
            if (inactivityTime.TotalSeconds > inactivityTimeout)
            {
                // Yeah, super dodgy
                KfClient.LastPacketReceived = DateTime.UtcNow;
                _logger.Error("Forcing disconnect and restart as bot is completely dead");
                await KfClient.DisconnectAsync();
                await KfClient.StartWsClient();
            }
        }
    }

    private async Task RefreshXfToken()
    {
        if (await _kfTokenService.IsLoggedIn())
        {
            _logger.Info("We were already logged in and should have a fresh cookie for chat now");
            // Only seems to happen if the bot thinks it's already logged in
            return;
        }

        var settings =
            await Helpers.GetMultipleValues([BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.KiwiFarmsPassword]);
        await _kfTokenService.PerformLogin(settings[BuiltIn.Keys.KiwiFarmsUsername].Value!,
            settings[BuiltIn.Keys.KiwiFarmsPassword].Value!);
        _logger.Info("Successfully logged in");
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        // Reset value to 0 as we've now successfully joined
        if (_joinFailures > 0) _joinFailures = 0;
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshDetectEnabled,
                BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.BotDisconnectReplayLimit])
            .Result;
        // Send messages if there are any to replay (Assuming we DC'd, and it's now the message flood)
        foreach (var replayMsg in SentMessages.Where(msg => msg.Status == SentMessageTrackerStatus.ChatDisconnected)
                     .TakeLast(settings[BuiltIn.Keys.BotDisconnectReplayLimit].ToType<int>()))
        {
            // Bypass the helpful method we have for sending messages so we don't create new sent message items for them
            // The validation of whether to send based on GambaSesh's presence etc. has already been performed for msgs here
            KfClient.SendMessage(replayMsg.Message);
            replayMsg.Status = SentMessageTrackerStatus.WaitingForResponse;
            replayMsg.SentAt = DateTimeOffset.UtcNow;
        }
        foreach(var lostMsg in SentMessages.Where(msg => msg.Status == SentMessageTrackerStatus.ChatDisconnected))
        {
            lostMsg.Status = SentMessageTrackerStatus.Lost;
        }
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            if (message.MessageEditDate == null)
            {
                _logger.Info($"KF ({message.MessageDate.ToLocalTime():HH:mm:ss}) <{message.Author.Username}> {message.Message}");
            }
            if (message.Author.Username == settings[BuiltIn.Keys.KiwiFarmsUsername].Value && message.MessageEditDate == null)
            {
                // MessageRaw is not actually REAL and RAW. The messages are still HTML encoded
                var decodedMessage = WebUtility.HtmlDecode(message.MessageRaw);
                var sentMessage = SentMessages.FirstOrDefault(sent =>
                    sent.Message == decodedMessage && sent.Status == SentMessageTrackerStatus.WaitingForResponse);
                if (sentMessage == null)
                {
                    _logger.Error("Received message from Sneedchat that I sent but have no idea about. Message Data Follows:");
                    _logger.Error(JsonSerializer.Serialize(message));
                    _logger.Error("Last item inserted into the sent messages collection waiting for response:");
                    var latest =
                        SentMessages.LastOrDefault(msg => msg.Status == SentMessageTrackerStatus.WaitingForResponse);
                    _logger.Error(JsonSerializer.Serialize(latest));
                    if (latest != null)
                    {
                        // Generally when you msg Sneedchat, the next message you get in response is your message echoed
                        // back to you. So this fallback should be generally correct and will account for the occasional
                        // mismatch due to messages not being 1:1 with what we thought we sent
                        _logger.Info("Just going to lazily associate it with the latest message");
                        latest.ChatMessageId = message.MessageId;
                        latest.Delay = DateTimeOffset.UtcNow - latest.SentAt;
                        latest.Status = SentMessageTrackerStatus.ResponseReceived;
                    }
                }
                else
                {
                    sentMessage.ChatMessageId = message.MessageId;
                    sentMessage.Delay = DateTimeOffset.UtcNow - sentMessage.SentAt;
                    sentMessage.Status = SentMessageTrackerStatus.ResponseReceived;
                }
            }

            if (message.Author.Id == settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>() && BotServices.TemporarilyBypassGambaSeshForDiscord &&
                message.MessageRaw.Contains("discord16"))
            {
                _logger.Info("GambaSesh fixed itself, turning off bypass");
                BotServices.TemporarilyBypassGambaSeshForDiscord = false;
            }
            if (settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean() && !InitialStartCooldown && message.Author.Id == settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>() && !GambaSeshPresent)
            {
                _logger.Info("Received a GambaSesh message after cooldown and while thinking he's not here. Setting the presence flag to avoid spamming chat");
                GambaSeshPresent = true;
            }

            // Basically the bot will ignore the message if it has been seen before and its edit time is the same
            // So this avoids reprocessing messages on reconnect while being able to handle edits, even if the edit came
            // during a disconnect / reconnect event
            if (!_seenMessages.Any(msg =>
                    msg.MessageId == message.MessageId && msg.LastEdited == message.MessageEditDate) &&
                !InitialStartCooldown)
            {
                _logger.Debug("Passing message to command interface");
                _botCommands.ProcessMessage(message);
            }

            // Update or add the element to keep it in sync
            var existingMsg = _seenMessages.FirstOrDefault(msg => msg.MessageId == message.MessageId);
            if (existingMsg != null)
            {
                existingMsg.LastEdited = message.MessageEditDate;
            }
            else
            {
                _seenMessages.Add(new SeenMessageMetadataModel {MessageId = message.MessageId, LastEdited = message.MessageEditDate});
            }
            UpdateUserLastActivityAsync(message.Author.Id, WhoWasActivityType.Message).Wait(_cancellationToken);
        }
        
        if (InitialStartCooldown) InitialStartCooldown = false;
    }

    // Reference for Sneedchat hardcoded length limit
    // https://github.com/jaw-sh/ruforo/blob/master/src/web/chat/connection.rs#L226
    public async Task<SentMessageTrackerModel> SendChatMessageAsync(string message, bool bypassSeshDetect = false, LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.TruncateNicely, int lengthLimit = 1023)
    {
        var settings = await Helpers
            .GetMultipleValues([
                BuiltIn.Keys.KiwiFarmsSuppressChatMessages, BuiltIn.Keys.GambaSeshDetectEnabled
            ]);
        var reference = Guid.NewGuid().ToString();
        var messageTracker = new SentMessageTrackerModel
        {
            Reference = reference,
            Message = message.TrimEnd(), // Sneedchat trims trailing spaces
            Status = SentMessageTrackerStatus.Unknown,
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

        if (!KfClient.IsConnected())
        {
            _logger.Info($"Not sending message '{message}' as Sneedchat is not connected");
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
        _logger.Debug($"Message is {messageTracker.Message.Utf8LengthBytes()} bytes");
        SentMessages.Add(messageTracker);
        await KfClient.SendMessageInstantAsync(messageTracker.Message);
        return messageTracker;
    }

    public SentMessageTrackerModel SendChatMessage(string message, bool bypassSeshDetect = false,
        LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.TruncateNicely, int lengthLimit = 1023)
    {
        return SendChatMessageAsync(message, bypassSeshDetect, lengthLimitBehavior, lengthLimit).Result;
    }

    // If you feed this long ass messages they will be eaten, don't be retarded.
    public async Task<List<SentMessageTrackerModel>> SendChatMessagesAsync(List<string> messages,
        bool bypassSeshDetect = false, LengthLimitBehavior lengthLimitBehavior = LengthLimitBehavior.RefuseToSend)
    {
        List<SentMessageTrackerModel> sentMessages = [];

        foreach (var message in messages)
        {
            sentMessages.Add(await SendChatMessageAsync(message, bypassSeshDetect, lengthLimitBehavior));
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

    public class SentMessageNotFoundException : Exception;
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled, BuiltIn.Keys.BotKeesSeen])
            .Result;
        _logger.Debug($"Received {users.Count} user join events");
        using var db = new ApplicationDbContext();
        foreach (var user in users)
        {
            if (user.Id == settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>() && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean())
            {
                _logger.Info("GambaSesh is now present");
                GambaSeshPresent = true;
            }

            if (user.Id == 89776 && !settings[BuiltIn.Keys.BotKeesSeen].ToBoolean())
            {
                _logger.Info("Kees has joined!");
                SendChatMessage($":!: :!: {user.Username} has appeared! :!: :!:", true);
                Helpers.SetValueAsBoolean(BuiltIn.Keys.BotKeesSeen, true).Wait(_cancellationToken);
            }
            _logger.Info($"{user.Username} joined!");

            var userDb = db.Users.FirstOrDefault(u => u.KfId == user.Id);
            if (userDb == null)
            {
                db.Users.Add(new UserDbModel { KfId = user.Id, KfUsername = user.Username });
                _logger.Debug("Adding user to DB");
                // Immediately add to DB so we can populate activity
                db.SaveChanges();
                UpdateUserLastActivityAsync(user.Id, WhoWasActivityType.Join).Wait(_cancellationToken);
                continue;
            }
            // Detect a username change
            if (userDb.KfUsername != user.Username)
            {
                _logger.Debug("Username has updated, updating DB");
                userDb.KfUsername = user.Username;
            }

            UpdateUserLastActivityAsync(user.Id, WhoWasActivityType.Join).Wait(_cancellationToken);
        }

        db.SaveChanges();
    }

    private void OnUsersParted(object sender, List<int> userIds)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled])
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

    private void OnKfWsDisconnected(object sender, DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Sneedchat disconnected due to {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
    }
    
    private void OnKfWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        var roomId = Helpers.GetValue(BuiltIn.Keys.KiwiFarmsRoomId).Result.ToType<int>();
        _logger.Error($"Sneedchat reconnected due to {reconnectionInfo.Type}");
        _logger.Info("Resetting GambaSesh presence so it can resync if he crashed while the bot was DC'd");
        GambaSeshPresent = false;
        _logger.Info($"Rejoining {roomId}");
        KfClient.JoinRoom(roomId);
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
}