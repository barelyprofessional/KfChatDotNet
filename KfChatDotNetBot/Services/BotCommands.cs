using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Commands;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
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

    internal BotCommands(ChatBot bot, CancellationToken ctx = default)
    {
        _cancellationToken = ctx;
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
                var continueAfterProcess = HasAttribute<AllowAdditionalMatches>(command);
                var kasinoCommand = HasAttribute<KasinoCommand>(command);
                var wagerCommand = HasAttribute<WagerCommand>(command);
                if (kasinoCommand)
                {
                    var kasinoEnabled = SettingsProvider.GetValueAsync(BuiltIn.Keys.MoneyEnabled).Result.ToBoolean();
                    if (!kasinoEnabled) return;
                }

                if (kasinoCommand && user.IsPermanentlyBanned(_cancellationToken).Result)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you've been permanently banned from the kasino. Contact support for more information.", true);
                    return;
                }

                if (wagerCommand)
                {
                    // GetGamblerEntity will only return null if the user is permanbanned
                    // and we have a check further up the chain for that hence ignoring the null
                    var exclusion = user.GetGamblerEntity(ct: _cancellationToken).Result
                        !.GetActiveExclusion(ct: _cancellationToken).Result;
                    if (exclusion != null)
                    {
                        _bot.SendChatMessage(
                            $"@{message.Author.Username}, you're self excluded from the kasino for another {(exclusion.Expires - DateTimeOffset.UtcNow).Humanize(precision: 3)}", true);
                        return;
                    }
                }
                if (user.UserRight < command.RequiredRight)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you do not have access to use this command. Your rank: {user.UserRight.Humanize()}; Required rank: {command.RequiredRight.Humanize()}", true);
                    if (continueAfterProcess) continue;
                    break;
                }
                _ = ProcessMessageAsync(command, message, user, match.Groups);
                if (!continueAfterProcess) break;
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
            return;
        }

        var moneySettings =
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.MoneyEnabled, BuiltIn.Keys.MoneySymbolSuffix]);
        if (!moneySettings[BuiltIn.Keys.MoneyEnabled].ToBoolean()) return;
        var wagerCommand = HasAttribute<WagerCommand>(command);
        if (!wagerCommand) return;
        var gambler = await user.GetGamblerEntity(ct: _cancellationToken);
        if (gambler == null) return;
        if (gambler.TotalWagered < gambler.NextVipLevelWagerRequirement) return;
        // The reason for doing this instead of passing in TotalWagered is that otherwise VIP levels might
        // get skipped if the user is a low VIP level but wagering very large amounts
        var newLevel = Money.GetNextVipLevel(gambler.NextVipLevelWagerRequirement);
        if (newLevel == null) return;
        var payout = await gambler.UpgradeVipLevel(newLevel, _cancellationToken);
        await _bot.SendChatMessageAsync(
            $"🤑🤑 {user.KfUsername} has leveled up to to {newLevel.VipLevel.Icon} {newLevel.VipLevel.Name} Tier {newLevel.Tier} " +
            $"and received a bonus of {payout:N2} {moneySettings[BuiltIn.Keys.MoneySymbolSuffix].Value}", true);
    }
    
    private static bool HasAttribute<T>(ICommand command) where T : Attribute
    {
        return Attribute.GetCustomAttribute(command.GetType(), typeof(T)) != null;
    }

}

/// <summary>
/// Normally if a command is matched and executed, the loop breaks and no further commands are processed
/// Use this attribute if you want to continue attempting to match and run other commands after this one
/// Keep in mind since commands are executed in a throwaway task and not awaited, they will run concurrently
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal class AllowAdditionalMatches : Attribute;

/// <summary>
/// Use this on commands where a wager is taking place.
/// This will cause the bot to check total wagered and see if the gambler has leveled up.
/// It'll also check whether the gambler is currently temp excluded before running the command.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal class WagerCommand : Attribute;

/// <summary>
/// Use this on all commands that interact with the gambling / monetary system
/// When used, this will check if the system is globally enabled before running the command.
/// It'll also check whether the user is permanently banned before running the command.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal class KasinoCommand : Attribute;