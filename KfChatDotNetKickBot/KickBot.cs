﻿using System.Text.Json;
using KfChatDotNetKickBot.Services;
using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using KickWsClient.Models;
using NLog;
using Websocket.Client;

namespace KfChatDotNetKickBot;

public class KickBot
{
    private readonly ChatClient _kfClient;
    private readonly KickWsClient.KickWsClient _kickClient;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly Models.ConfigModel _config;
    private readonly bool _pingEnabled = true;
    private bool _gambaSeshPresent;
    private string _xfSessionToken = null!;
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private readonly List<int> _seenMsgIds = [];
    // Suppresses the command handler on initial start, so it doesn't pick up things already handled on restart
    private bool _initialStartCooldown = true;
    private readonly CancellationToken _cancellationToken = new();
    
    public KickBot()
    {
        _logger.Info("Bot starting!");
        const string configPath = "config.json";
        if (!Path.Exists(configPath))
        {
            _logger.Error($"{configPath} is missing! Exiting");
            Environment.Exit(1);
        }

        _config = JsonSerializer.Deserialize<Models.ConfigModel>(File.ReadAllText(configPath)) ??
                  throw new InvalidOperationException();
        RefreshXfToken().Wait(_cancellationToken);
        
        _kfClient = new ChatClient(new ChatClientConfigModel
        {
            WsUri = _config.KfWsEndpoint,
            XfSessionToken = _xfSessionToken,
            CookieDomain = _config.KfWsEndpoint.Host,
            Proxy = _config.KfProxy,
            ReconnectTimeout = _config.KfReconnectTimeout
        });

        _kickClient = new KickWsClient.KickWsClient(_config.PusherEndpoint.ToString(),
            _config.PusherProxy, _config.PusherReconnectTimeout);

        _kfClient.OnMessages += OnKfChatMessage;
        _kfClient.OnUsersParted += OnUsersParted;
        _kfClient.OnUsersJoined += OnUsersJoined;
        _kfClient.OnWsDisconnection += OnKfWsDisconnected;
        _kfClient.OnWsReconnect += OnKfWsReconnected;
        _kfClient.OnFailedToJoinRoom += OnFailedToJoinRoom;

        _kickClient.OnStreamerIsLive += OnStreamerIsLive;
        _kickClient.OnChatMessage += OnKickChatMessage;
        _kickClient.OnWsReconnect += OnPusherWsReconnected;
        _kickClient.OnPusherSubscriptionSucceeded += OnPusherSubscriptionSucceeded;
        _kickClient.OnStopStreamBroadcast += OnStopStreamBroadcast;

        _kfClient.StartWsClient().Wait(_cancellationToken);

        _kickClient.StartWsClient().Wait(_cancellationToken);
        foreach (var channel in _config.PusherChannels)
        {
            _kickClient.SendPusherSubscribe(channel);
        }

        _logger.Debug("Creating ping thread and starting it");
        var pingThread = new Thread(PingThread);
        pingThread.Start();
        
        while (true)
        {
            Console.ReadLine();
        }
    }

    private void OnFailedToJoinRoom(object sender, string message)
    {
        _logger.Error($"Couldn't join the room. KF returned: {message}");
        _logger.Error("This is likely due to the session cookie expiring. Retrieving a new one.");
        RefreshXfToken().Wait(_cancellationToken);
        _kfClient.UpdateToken(_xfSessionToken);
        _logger.Info("Retrieved fresh token. Reconnecting.");
        _kfClient.Disconnect();
        _kfClient.StartWsClient().Wait(_cancellationToken);
        _logger.Info("Client should be reconnecting now");
    }

    private void PingThread()
    {
        while (_pingEnabled)
        {
            Thread.Sleep(TimeSpan.FromSeconds(15));
            _logger.Debug("Pinging KF and Pusher");
            _kfClient.SendMessage("/ping");
            _kickClient.SendPusherPing();
            if (_initialStartCooldown) _initialStartCooldown = false;
        }
    }

    private async Task RefreshXfToken()
    {
        var cookie = await KfTokenService.FetchSessionTokenAsync(_config.KfDomain, _config.KfUsername, _config.KfPassword,
            _config.ChromiumPath, _config.KfProxy);
        _logger.Debug($"FetchSessionTokenAsync returned {cookie}");
        _xfSessionToken = cookie;
    }

    private void OnStreamerIsLive(object sender, KickModels.StreamerIsLiveEventModel? e)
    {
        _sendChatMessage($"Bossman Live! {e?.Livestream.SessionTitle} https://kick.com/bossmanjack");
    }

    private void OnStopStreamBroadcast(object sender, KickModels.StopStreamBroadcastEventModel? e)
    {
        _sendChatMessage("The stream is so over. :lossmanjack:");
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            _logger.Info($"KF ({message.MessageDate.ToLocalTime():HH:mm:ss}) <{message.Author.Username}> {message.Message}");
            if (!_seenMsgIds.Contains(message.MessageId) && !_initialStartCooldown)
            {
                if (message.MessageRaw.StartsWith("!time"))
                {
                    var bmt = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset);
                    _sendChatMessage($"It's currently {bmt:h:mm:ss tt} BMT");
                }
                else if (message.MessageRaw.StartsWith("!twisted"))
                {
                    _sendChatMessage("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
                }
                else if (message.MessageRaw.StartsWith("!insanity"))
                {
                    // ReSharper disable once StringLiteralTypo
                    _sendChatMessage("definition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEFdefinition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEF");
                }
                else if (message.MessageRaw.StartsWith("!helpme"))
                {
                    _sendChatMessage("[img]https://i.postimg.cc/fTw6tGWZ/ineedmoneydumbfuck.png[/img]", true);
                }
            }
            else
            {
                _logger.Debug($"_seenMsgIds check => {!_seenMsgIds.Contains(message.MessageId)}, _initialStartCooldown => {_initialStartCooldown}");
            }
            _seenMsgIds.Add(message.MessageId);
        }
    }

    private void _sendChatMessage(string message, bool bypassSeshDetect = false)
    {
        if (_gambaSeshPresent && _config.EnableGambaSeshDetect && !bypassSeshDetect)
        {
            _logger.Info($"Not sending message '{message}' as GambaSesh is present");
            return;
        }

        _kfClient.SendMessage(message);
    }

    private void OnKickChatMessage(object sender, KickModels.ChatMessageEventModel? e)
    {
        if (e == null) return; 
        _logger.Info($"Kick ({e.CreatedAt.LocalDateTime.ToLocalTime():HH:mm:ss}) <{e.Sender.Username}> {e.Content}");
        _logger.Debug($"BB Code Translation: {e.Content.TranslateKickEmotes()}");

        if (e.Sender.Slug != "bossmanjack") return;
        
        _logger.Debug("Message from BossmanJack");
        _sendChatMessage($"[img]{_config.KickIcon}[/img] BossmanJack: {e.Content.TranslateKickEmotes()}");
    }
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        _logger.Debug($"Received {users.Count} user join events");
        foreach (var user in users)
        {
            if (user.Id == _config.GambaSeshUserId && _config.EnableGambaSeshDetect)
            {
                _logger.Info("GambaSesh is now present");
                _gambaSeshPresent = true;
            }
            _logger.Info($"{user.Username} joined!");
        }
    }

    private void OnUsersParted(object sender, List<int> userIds)
    {
        if (userIds.Contains(_config.GambaSeshUserId) && _config.EnableGambaSeshDetect)
        {
            _logger.Info("GambaSesh is no longer present");
            _gambaSeshPresent = false;
        }
    }

    private void OnKfWsDisconnected(object sender, DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Sneedchat disconnected due to {disconnectionInfo.Type}");
    }
    
    private void OnKfWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Sneedchat reconnected due to {reconnectionInfo.Type}");
        _logger.Info($"Rejoining {_config.KfChatRoomId}");
        _kfClient.JoinRoom(_config.KfChatRoomId);
    }
    
    private void OnPusherWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Pusher reconnected due to {reconnectionInfo.Type}");
        foreach (var channel in _config.PusherChannels)
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