using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Commands;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Services;

// Took one look at GambaSesh's code, and it made my head explode
// This implementation is inspired by similar bot I wrote years ago
internal class BotCommands
{
    private ChatBot _bot;
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private char CommandPrefix = '!';
    private IEnumerable<ICommand> Commands;
    private CancellationToken _cancellationToken;

    internal BotCommands(ChatBot bot, CancellationToken? ctx = null)
    {
        _cancellationToken = ctx ?? CancellationToken.None;
        _bot = bot;
        var interfaceType = typeof(ICommand);
        Commands =
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => interfaceType.IsAssignableFrom(x) && x is { IsInterface: false, IsAbstract: false })
                .Select(Activator.CreateInstance).Cast<ICommand>();

        foreach (var command in Commands)
        {
            _logger.Debug($"Found command {command.GetType().Name}");
        }
    }

    internal void ProcessMessage(MessageModel message)
    {
        if (string.IsNullOrEmpty(message.MessageRaw))
        {
            return;
        }

        if (!message.MessageRaw.StartsWith(CommandPrefix))
        {
            return;
        }

        var messageTrimmed = message.MessageRaw.TrimStart(CommandPrefix);
        foreach (var command in Commands)
        {
            foreach (var regex in command.Patterns)
            {
                var match = regex.Match(messageTrimmed);
                if (!match.Success) continue;
                _logger.Debug($"Message matches {regex}");
                using var db = new ApplicationDbContext();
                var user = db.Users.FirstOrDefault(u => u.KfId == message.Author.Id);
                // This should never happen as brand-new users are created upon join
                if (user == null) return;
                if (user.Ignored) return;
                if (user.UserRight < command.RequiredRight)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you do not have access to use this command. Your rank: {user.UserRight.Humanize()}; Required rank: {command.RequiredRight.Humanize()}", true);
                    break;
                }
                _ = ProcessMessageAsync(command, message, user, match.Groups);
                break;
            }
        }
    }

    private async Task ProcessMessageAsync(ICommand command, MessageModel message, UserDbModel user, GroupCollection arguments)
    {
        var task = Task.Run(() => command.RunCommand(_bot, message, user, arguments, _cancellationToken), _cancellationToken);
        try
        {
            await task.WaitAsync(command.Timeout, _cancellationToken);
        }
        catch (Exception e)
        {
            _logger.Error("Caught an exception while waiting for the command to complete");
            _logger.Error(e);
            return;
        }
        if (task.IsFaulted)
        {
            _logger.Error("Command task failed");
            _logger.Error(task.Exception);
        }
    }
}