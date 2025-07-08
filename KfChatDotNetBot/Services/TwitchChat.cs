using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using Websocket.Client;

namespace KfChatDotNetBot.Services;

public class TwitchChat : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private WebsocketClient? _wsClient;
    private readonly Uri _wsUri = new("wss://irc-ws.chat.twitch.tv/");
    private const int ReconnectTimeout = 600;
    private readonly string? _proxy;
    private readonly string _channel;
    private readonly string _nick;

    public delegate void MessageReceivedEventHandler(object sender, string nick, string target, string message);
    public delegate void WsDisconnectionEventHandler(object sender, DisconnectionInfo e);
    public event MessageReceivedEventHandler? OnMessageReceived;
    public event WsDisconnectionEventHandler? OnWsDisconnection;

    private readonly CancellationToken _cancellationToken;

    public TwitchChat(string channel, string? proxy = null, CancellationToken cancellationToken = default)
    {
        _proxy = proxy;
        _cancellationToken = cancellationToken;
        _channel = channel;
        var justinFan = new Random().Next(10000, 99999);
        _nick = $"justinfan{justinFan}";
        _logger.Debug($"Using nick {_nick}");
        _logger.Info("Twitch Chat Service created");
    }
    
    public async Task StartWsClient()
    {
        _logger.Debug("StartWsClient() called, creating client");
        await CreateWsClient();
    }

    private async Task CreateWsClient()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var clientWs = new ClientWebSocket();
            if (_proxy == null) return clientWs;
            _logger.Debug($"Using proxy address {_proxy}");
            clientWs.Options.Proxy = new WebProxy(_proxy);
            return clientWs;
        });
        
        var client = new WebsocketClient(_wsUri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(ReconnectTimeout),
            IsReconnectionEnabled = false
        };
        
        client.ReconnectionHappened.Subscribe(WsReconnection);
        client.MessageReceived.Subscribe(WsMessageReceived);
        client.DisconnectionHappened.Subscribe(WsDisconnection);

        _wsClient = client;
        
        _logger.Debug("Websocket client has been built, about to start");
        await client.Start();
        _logger.Debug("Websocket client started!");
    }
    
    public bool IsConnected()
    {
        return _wsClient is { IsRunning: true };
    }
    
    private void WsDisconnection(DisconnectionInfo disconnectionInfo)
    {
        _logger.Error($"Client disconnected from Discord (or never successfully connected). Type is {disconnectionInfo.Type}");
        _logger.Error($"Close Status => {disconnectionInfo.CloseStatus}; Close Status Description => {disconnectionInfo.CloseStatusDescription}");
        _logger.Error(disconnectionInfo.Exception);
        OnWsDisconnection?.Invoke(this, disconnectionInfo);
    }
    
    private void WsReconnection(ReconnectionInfo reconnectionInfo)
    {
        _logger.Error($"Websocket connection dropped and reconnected. Reconnection type is {reconnectionInfo.Type}");
        _logger.Info("Sending registration info to Twitch IRC");
        // I've found if you use the message queue then things come out of order, hence using SendInstant
        _wsClient?.SendInstant("CAP REQ :twitch.tv/tags twitch.tv/commands").Wait(_cancellationToken);
        // Would be an oauth token if you were signed in, but this is just guest access
        _wsClient?.SendInstant("PASS SCHMOOPIIE").Wait(_cancellationToken);
        // Guest users are just justinfan12345 where the 5 digits are random
        _wsClient?.SendInstant($"NICK {_nick}").Wait(_cancellationToken);
        // I'm ashamed I've forgotten so much IRC protocol shit that I can't remember what the USER params mean :(
        _wsClient?.SendInstant($"USER {_nick} 8 * :{_nick}").Wait(_cancellationToken);
    }
    
    private void WsMessageReceived(ResponseMessage message)
    {
        if (message.Text == null)
        {
            _logger.Info("Twitch sent a null message");
            return;
        }

        _logger.Debug($"Received message from Twitch IRC: {message.Text}");

        try
        {
            if (message.Text.Contains("PRIVMSG"))
            {
                // This regex basically ignores all the IRCv3 stuff so handles PRIVMSG fine
                var privmsgRegex =
                    new Regex(
                        ":(?<nick>[^ ]+?)\\!(?<user>[^ ]+?)@(?<host>[^ ]+?) PRIVMSG (?<target>[^ ]+?) :(?<message>.*)");
                var privmsg = privmsgRegex.Match(message.Text);
                if (!privmsg.Success)
                {
                    throw new InvalidOperationException("PRIVMSG regex failed to match");
                }
                
                OnMessageReceived?.Invoke(this, privmsg.Groups["nick"].Value, privmsg.Groups["target"].Value,
                    privmsg.Groups["message"].Value);
                return;
            }

            // These are generally filled with IRCv3 gobbledegook and I don't care if it's not a PRIVMSG
            if (message.Text.StartsWith('@'))
            {
                _logger.Debug("Ignoring non-PRIVMSG IRCv3 filled junk");
                return;
            }
            // This regex is pretty good for parsing most messages but chokes hard on some Twitch IRCv3 insanity
            var ircMessageRegex =
                new Regex(
                    "(?::(?<Prefix>[^ ]+) +)?(?<Command>[^ :]+)(?<middle>(?: +[^ :]+))*(?<coda> +:(?<trailing>.*)?)?");
            var ircMessageMatch = ircMessageRegex.Match(message.Text);
            if (!ircMessageMatch.Success)
            {
                throw new InvalidOperationException("Failed to match IRC message");
            }
            var command = ircMessageMatch.Groups["Command"].Value;
            var trailing = ircMessageMatch.Groups["trailing"].Value;
            _logger.Debug($"Received command {command} with trailing: {trailing}");
            switch (command)
            {
                case "PING":
                    _logger.Info("Received PING, sending PONG");
                    _wsClient?.Send("PONG");
                    return;
                case "JOIN":
                    _logger.Debug("Received JOIN response");
                    return;
                // MOTD
                case "001":
                    _logger.Debug("Received MOTD. Sending JOIN");
                    _wsClient?.Send($"JOIN {_channel}");
                    return;
                default:
                    _logger.Debug($"Command {command} was not handled");
                    return;
            }
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle message from Twitch IRC");
            _logger.Error(e);
            _logger.Error("--- IRC Message ---");
            _logger.Error(message.Text);
            _logger.Error("--- End of IRC Message ---");
        }
    }

    public void Dispose()
    {
        _wsClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}