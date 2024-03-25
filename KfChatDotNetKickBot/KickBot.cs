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
        
    }

    private void OnKfChatMessage(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            AnsiConsole.MarkupLine($"[yellow]KF[/] <{message.Author.Username}> {message.Message.EscapeMarkup()} ({message.MessageDate.LocalDateTime.ToShortTimeString()})");
        }
    }

    private void OnKickChatMessage(object sender, KickModels.ChatMessageEventModel? e)
    {
        if (e == null) return;
        AnsiConsole.MarkupLine($"[green]Kick[/] <{e.Sender.Username}> {e.Content.EscapeMarkup()} ({e.CreatedAt.LocalDateTime.ToShortTimeString()})");

    }
    
    private void OnUsersJoined(object sender, List<UserModel> users, UsersJsonModel jsonPayload)
    {
        _logger.Debug($"Received {users.Count} user join events");
        foreach (var user in users)
        {
            AnsiConsole.MarkupLine($"[green]{user.Username.EscapeMarkup()} joined![/]");
        }
    }

    private void OnUsersParted(object sender, List<int> userIds)
    {
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