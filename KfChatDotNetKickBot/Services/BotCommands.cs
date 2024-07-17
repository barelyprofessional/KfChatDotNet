using KfChatDotNetKickBot.Commands;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetKickBot.Services;

// Took one look at GambaSesh's code, and it made my head explode
// This implementation is inspired by similar bot I wrote years ago
internal class BotCommands
{
    private KickBot _bot;
    private Logger _logger = LogManager.GetCurrentClassLogger();
    private char CommandPrefix = '!';
    private IEnumerable<ICommand> Commands;
    private CancellationToken _cancellationToken;
    private List<Task> _commandTasks = [];

    internal BotCommands(KickBot bot, CancellationToken? ctx = null)
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
                if (user.UserRight < command.RequiredRight)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you do not have access to use this command.", true);
                    break;
                }
                var task = Task.Run(() => command.RunCommand(_bot, message, match.Groups, _cancellationToken), _cancellationToken);
                _commandTasks.Add(task);
            }
        }
        
        // Check on the state of the tasks, there's no way to know what error they produce if they failed otherwise
        List<Task> removals = [];
        foreach (var task in _commandTasks)
        {
            if (!task.IsCompleted) continue;
            if (task.IsFaulted)
            {
                _logger.Error("Command task failed at some point");
                _logger.Error(task.Exception);
            }

            removals.Add(task);
        }
        // .NET doesn't support modifying a collection you're iterating over
        foreach (var removal in removals)
        {
            _commandTasks.Remove(removal);
        }
    }
}