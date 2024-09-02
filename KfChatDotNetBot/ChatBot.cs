﻿using System.Net;
using System.Text.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot;

public class ChatBot
{
    internal readonly ChatClient KfClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private readonly List<int> _seenMsgIds = [];
    // Suppresses the command handler on initial start, so it doesn't pick up things already handled on restart
    internal bool InitialStartCooldown = true;
    private readonly CancellationToken _cancellationToken = new();
    private readonly BotCommands _botCommands;
    private readonly List<SentMessageTrackerModel> _sentMessages = [];
    internal bool GambaSeshPresent;
    internal readonly BotServices BotServices;
    private Task _kfChatPing;
    private KfTokenService _kfTokenService;
    
    public ChatBot()
    {
        _logger.Info("Bot starting!");

        var settings = Helpers.GetMultipleValues([
            BuiltIn.Keys.KiwiFarmsWsEndpoint, BuiltIn.Keys.KiwiFarmsDomain,
            BuiltIn.Keys.Proxy, BuiltIn.Keys.KiwiFarmsWsReconnectTimeout]).Result;

        _kfTokenService = new KfTokenService(settings[BuiltIn.Keys.KiwiFarmsDomain].Value!,
            settings[BuiltIn.Keys.Proxy].Value, _cancellationToken);
        if (_kfTokenService.GetXfSessionCookie() == null)
        {
            RefreshXfToken().Wait(_cancellationToken);
        }
        
        KfClient = new ChatClient(new ChatClientConfigModel
        {
            WsUri = new Uri(settings[BuiltIn.Keys.KiwiFarmsWsEndpoint].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsWsEndpoint} cannot be null")),
            XfSessionToken = _kfTokenService.GetXfSessionCookie(),
            CookieDomain = settings[BuiltIn.Keys.KiwiFarmsDomain].Value ?? throw new InvalidOperationException($"{BuiltIn.Keys.KiwiFarmsDomain} cannot be null"),
            Proxy = settings[BuiltIn.Keys.Proxy].Value,
            ReconnectTimeout = settings[BuiltIn.Keys.KiwiFarmsWsReconnectTimeout].ToType<int>()
        });
  
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
        _logger.Error($"Couldn't join the room. KF returned: {message}");
        _logger.Error("This is likely due to the session cookie expiring. Retrieving a new one.");
        RefreshXfToken().Wait(_cancellationToken);
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            _logger.Debug("Pinging KF");
            KfClient.SendMessage("/ping");
            if (InitialStartCooldown) InitialStartCooldown = false;
            var inactivityTime = DateTime.UtcNow - KfClient.LastPacketReceived;
            _logger.Debug($"Last KF event was {inactivityTime:g} ago");
            if (inactivityTime.TotalMinutes > 10)
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
            KfClient.UpdateToken(_kfTokenService.GetXfSessionCookie()!);
            await _kfTokenService.SaveCookies();
            return;
        }

        var settings =
            await Helpers.GetMultipleValues([BuiltIn.Keys.KiwiFarmsUsername, BuiltIn.Keys.KiwiFarmsPassword]);
        await _kfTokenService.PerformLogin(settings[BuiltIn.Keys.KiwiFarmsUsername].Value!,
            settings[BuiltIn.Keys.KiwiFarmsPassword].Value!);
        KfClient.UpdateToken(_kfTokenService.GetXfSessionCookie()!);
        await _kfTokenService.SaveCookies();
        _logger.Info("Successfully logged in");
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshDetectEnabled,
                BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.KiwiFarmsUsername])
            .Result;
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            _logger.Info($"KF ({message.MessageDate.ToLocalTime():HH:mm:ss}) <{message.Author.Username}> {message.Message}");
            if (message.Author.Username == settings[BuiltIn.Keys.KiwiFarmsUsername].Value && message.MessageEditDate == null)
            {
                // MessageRaw is not actually REAL and RAW. The messages are still HTML encoded
                var decodedMessage = WebUtility.HtmlDecode(message.MessageRaw);
                var sentMessage = _sentMessages.FirstOrDefault(sent =>
                    sent.Message == decodedMessage && sent.Status == SentMessageTrackerStatus.WaitingForResponse);
                if (sentMessage == null)
                {
                    _logger.Error("Received message from Sneedchat that I sent but have no idea about. Message Data Follows:");
                    _logger.Error(JsonSerializer.Serialize(message));
                    _logger.Error("Last item inserted into the sent messages collection:");
                    _logger.Error(JsonSerializer.Serialize(_sentMessages.LastOrDefault()));
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
            if (!_seenMsgIds.Contains(message.MessageId) && !InitialStartCooldown)
            {
                _logger.Debug("Passing message to command interface");
                _botCommands.ProcessMessage(message);
            }
            else
            {
                _logger.Debug($"_seenMsgIds check => {!_seenMsgIds.Contains(message.MessageId)}, _initialStartCooldown => {InitialStartCooldown}");
            }
            _seenMsgIds.Add(message.MessageId);
        }
    }

    public string SendChatMessage(string message, bool bypassSeshDetect = false)
    {
        var settings = Helpers
            .GetMultipleValues([BuiltIn.Keys.KiwiFarmsSuppressChatMessages, BuiltIn.Keys.GambaSeshDetectEnabled])
            .Result;
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
            _sentMessages.Add(messageTracker);
            return reference;
        }
        if (GambaSeshPresent && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean() && !bypassSeshDetect)
        {
            _logger.Info($"Not sending message '{message}' as GambaSesh is present");
            messageTracker.Status = SentMessageTrackerStatus.NotSending;
            _sentMessages.Add(messageTracker);
            return reference;
        }
        messageTracker.Status = SentMessageTrackerStatus.WaitingForResponse;
        messageTracker.SentAt = DateTimeOffset.UtcNow;
        _sentMessages.Add(messageTracker);
        KfClient.SendMessage(message);
        return reference;
    }

    public SentMessageTrackerModel GetSentMessageStatus(string reference)
    {
        var message = _sentMessages.FirstOrDefault(m => m.Reference == reference);
        if (message == null)
        {
            throw new SentMessageNotFoundException();
        }

        return message;
    }

    public class SentMessageNotFoundException : Exception;
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        var settings = Helpers.GetMultipleValues([BuiltIn.Keys.GambaSeshUserId, BuiltIn.Keys.GambaSeshDetectEnabled])
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
        if (userIds.Contains(settings[BuiltIn.Keys.GambaSeshUserId].ToType<int>()) && settings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean())
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
        var roomId = Helpers.GetValue(BuiltIn.Keys.KiwiFarmsRoomId).Result.ToType<int>();
        _logger.Error($"Sneedchat reconnected due to {reconnectionInfo.Type}");
        _logger.Info("Resetting GambaSesh presence so it can resync if he crashed while the bot was DC'd");
        GambaSeshPresent = false;
        _logger.Info($"Rejoining {roomId}");
        KfClient.JoinRoom(roomId);
    }
}