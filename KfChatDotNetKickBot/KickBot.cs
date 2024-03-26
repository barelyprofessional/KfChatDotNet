using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using KickWsClient.Models;
using Newtonsoft.Json;
using NLog;
using Spectre.Console;
using Websocket.Client;

namespace KfChatDotNetKickBot;

public class KickBot
{
    private ChatClient _kfClient;
    private KickWsClient.KickWsClient _kickClient;
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private Models.ConfigModel _config;
    private Thread _pingThread;
    private bool _pingEnabled = true;
    private bool _gambaSeshPresent = false;
    // Oh no it's an ever expanding list that may never get cleaned up!
    // BUY MORE RAM
    private List<int> _seenMsgIds = new List<int>();
    // Suppresses the command handler on initial start so it doesn't pick up things already handled on restart
    private bool _initialStartCooldown = true;

    public KickBot()
    {
        _logger.Info("Bot starting!");
        const string configPath = "config.json";
        if (!Path.Exists(configPath))
        {
            _logger.Error($"{configPath} is missing! Exiting");
            Environment.Exit(1);
        }

        _config = JsonConvert.DeserializeObject<Models.ConfigModel>(File.ReadAllText(configPath)) ??
                  throw new InvalidOperationException();
        
        _kfClient = new ChatClient(new ChatClientConfigModel
        {
            WsUri = _config.KfWsEndpoint,
            XfSessionToken = GetXfToken(),
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

        _kickClient.OnStreamerIsLive += OnStreamerIsLive;
        _kickClient.OnChatMessage += OnKickChatMessage;
        _kickClient.OnWsReconnect += OnPusherWsReconnected;
        _kickClient.OnPusherSubscriptionSucceeded += OnPusherSubscriptionSucceeded;
        _kickClient.OnStopStreamBroadcast += OnStopStreamBroadcast;

        _kfClient.StartWsClient().Wait();
        _kfClient.JoinRoom(_config.KfChatRoomId);

        _kickClient.StartWsClient().Wait();
        foreach (var channel in _config.PusherChannels)
        {
            _kickClient.SendPusherSubscribe(channel);
        }

        _pingThread = new Thread(PingThread);
        _pingThread.Start();
        
        while (true)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("Enter Message:"));
            _kfClient.SendMessage(input);
        }
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

    private string GetXfToken()
    {
        //return Helpers.GetXfToken("xf_session", _config.KfWsEndpoint.Host, _config.FirefoxCookieContainer).Result ??
        //       throw new InvalidOperationException();
        return _config.XfTokenValue;
    }

    private void OnStreamerIsLive(object sender, KickModels.StreamerIsLiveEventModel? e)
    {
        if (_config.EnableGambaSeshDetect && _gambaSeshPresent)
        {
            AnsiConsole.MarkupLine("[red]Suppressing live stream notification as GambaSesh is present[/]");
            return;
        }
        
        AnsiConsole.MarkupLine("[green]Streamer is live!!![/]");
        _kfClient.SendMessage($"Bossman Live! {e?.Livestream.SessionTitle} https://kick.com/bossmanjack [i]This action was automated");
    }

    private void OnStopStreamBroadcast(object sender, KickModels.StopStreamBroadcastEventModel? e)
    {
        if (_config.EnableGambaSeshDetect && _gambaSeshPresent)
        {
            AnsiConsole.MarkupLine("[red]Suppressing live stream notification as GambaSesh is present[/]");
            return;
        }
        
        AnsiConsole.MarkupLine("[green]Stream stopped!!![/]");
        _kfClient.SendMessage("The stream is so over. [i]This action was automated");
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            AnsiConsole.MarkupLine($"[yellow]KF[/] <{message.Author.Username}> {message.Message.EscapeMarkup()} ({message.MessageDate.LocalDateTime.ToShortTimeString()})");
            if (!(!_gambaSeshPresent && _config.EnableGambaSeshDetect) && !_seenMsgIds.Contains(message.MessageId) && !_initialStartCooldown)
            {
                if (message.MessageRaw.StartsWith("!time"))
                {
                    var bmt = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset);
                    _kfClient.SendMessage($"It's currently {bmt:h:mm:ss tt} BMT");
                }
                else if (message.MessageRaw.StartsWith("!twisted"))
                {
                    _kfClient.SendMessage("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
                }
                else if (message.MessageRaw.StartsWith("!insanity"))
                {
                    _kfClient.SendMessage("definition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEFdefinition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEF");
                }
            }
            _seenMsgIds.Add(message.MessageId);
        }
    }

    private void OnKickChatMessage(object sender, KickModels.ChatMessageEventModel? e)
    {
        if (e == null) return;
        AnsiConsole.MarkupLine($"[green]Kick[/] <{e.Sender.Username}> {e.Content.EscapeMarkup()} ({e.CreatedAt.LocalDateTime.ToShortTimeString()})");
        AnsiConsole.MarkupLine($"[cyan]BB Code Translation: {e.Content.TranslateKickEmotes().EscapeMarkup()}[/]");
        if (_gambaSeshPresent && _config.EnableGambaSeshDetect)
        {
            return;
        }

        if (e.Sender.Slug == "bossmanjack")
        {
            AnsiConsole.MarkupLine("[green]Message from BossmanJack[/]");
            _kfClient.SendMessage($"[img]{_config.KickIcon}[/img] BossmanJack: {e.Content.TranslateKickEmotes()}");
        }
    }
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        _logger.Debug($"Received {users.Count} user join events");
        foreach (var user in users)
        {
            if (user.Id == _config.GambaSeshUserId && _config.EnableGambaSeshDetect)
            {
                _gambaSeshPresent = true;
            }
            AnsiConsole.MarkupLine($"[green]{user.Username.EscapeMarkup()} joined![/]");
        }
    }

    private void OnUsersParted(object sender, List<int> userIds)
    {
        if (userIds.Contains(_config.GambaSeshUserId) && _config.EnableGambaSeshDetect)
        {
            _gambaSeshPresent = false;
        }
        _logger.Debug($"Received {userIds.Count} user part events");
        foreach (var id in userIds)
        {
            AnsiConsole.MarkupLine($"[red]{id} left the chat...[/]");
        }
    }

    private void OnKfWsDisconnected(object sender, DisconnectionInfo disconnectionInfo)
    {
        AnsiConsole.MarkupLine($"[red]Sneedchat disconnected due to {disconnectionInfo.Type}[/]");
        AnsiConsole.MarkupLine("[yellow]Grabbing fresh token from browser[/]");
        var token = GetXfToken();
        AnsiConsole.MarkupLine($"[green]Obtained token = {token.EscapeMarkup()}[/]");
        _kfClient.UpdateToken(token);
    }
    
    private void OnKfWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        AnsiConsole.MarkupLine($"[red]Sneedchat reconnected due to {reconnectionInfo.Type}[/]");
        AnsiConsole.MarkupLine($"[green]Rejoining {_config.KfChatRoomId}[/]");
        _kfClient.JoinRoom(_config.KfChatRoomId);
    }
    
    private void OnPusherWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        AnsiConsole.MarkupLine($"[red]Pusher reconnected due to {reconnectionInfo.Type}[/]");
        foreach (var channel in _config.PusherChannels)
        {
            AnsiConsole.MarkupLine($"[green]Rejoining {channel}[/]");
            _kickClient.SendPusherSubscribe(channel);
        }
    }

    private void OnPusherSubscriptionSucceeded(object sender, PusherModels.BasePusherEventModel? e)
    {
        AnsiConsole.MarkupLine($"[green]Pusher indicates subscription to {e?.Channel.EscapeMarkup()} was successful[/]");
    }
}