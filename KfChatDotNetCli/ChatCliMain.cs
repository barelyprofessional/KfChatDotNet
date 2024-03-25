using KfChatDotNetWsClient;
using KfChatDotNetWsClient.Models;
using KfChatDotNetWsClient.Models.Events;
using KfChatDotNetWsClient.Models.Json;
using NLog;
using Spectre.Console;
using Websocket.Client;

namespace KfChatDotNetCli;

public class ChatCliMain
{
    private ChatClient _client;
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private int _roomId;
    
    public ChatCliMain(string xfSessionToken, int roomId)
    {
        _roomId = roomId;
        _client = new ChatClient(new ChatClientConfigModel
        {
            WsUri = new Uri("wss://kiwifarms.st/chat.ws"),
            XfSessionToken = xfSessionToken
        });

        _client.OnMessages += OnMessages;
        _client.OnDeleteMessages += OnDeleteMessages;
        _client.OnUsersJoined += OnUsersJoined;
        _client.OnUsersParted += OnUsersParted;
        _client.OnWsReconnect += OnWsReconnected;
        
        _client.StartWsClient().Wait();
        _client.JoinRoom(_roomId);

        while (true)
        {
            var input = AnsiConsole.Prompt(new TextPrompt<string>("Enter Message:"));
            _client.SendMessage(input);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private void OnMessages(object sender, List<MessageModel> messages, MessagesJsonModel jsonPayload)
    {
        _logger.Debug($"Received {messages.Count} message(s)");
        foreach (var message in messages)
        {
            AnsiConsole.MarkupLine($"<{message.Author.Username}> {message.Message.EscapeMarkup()} ({message.MessageDate.LocalDateTime.ToShortTimeString()})");
        }
    }

    private void OnDeleteMessages(object sender, List<int> messageIds)
    {
        _logger.Debug($"Received delete event for {messageIds}");
        foreach (var id in messageIds)
        {
            AnsiConsole.MarkupLine($"[red]{id} message deleted![/]");
        }
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

    private void OnWsReconnected(object sender, ReconnectionInfo reconnectionInfo)
    {
        AnsiConsole.MarkupLine($"[red]Reconnected due to {reconnectionInfo.Type}[/]");
        AnsiConsole.MarkupLine($"[green]Rejoining {_roomId}[/]");
        _client.JoinRoom(_roomId);
    }
}