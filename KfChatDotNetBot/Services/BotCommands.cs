using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Commands;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
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

        _ = CleanupExpiredRateLimitEntriesTask();
    }

    internal void ProcessMessage(MessageModel message)
    {
        if (string.IsNullOrEmpty(message.MessageRaw))
        {
            return;
        }

        var messageTrimmed = message.MessageRaw.TrimStart(CommandPrefix);
        foreach (var command in Commands)
        {
            var noPrefixCommand = HasAttribute<NoPrefixRequired>(command);
            if (!noPrefixCommand && !message.MessageRaw.StartsWith(CommandPrefix)) continue;
            foreach (var regex in command.Patterns)
            {
                var match = regex.Match(messageTrimmed);
                if (!match.Success) continue;
                _logger.Debug($"Message matches {regex}");
                using var db = new ApplicationDbContext();
                var user = db.Users.AsNoTracking().FirstOrDefault(u => u.KfId == message.Author.Id);
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

                if (kasinoCommand && Money.IsPermanentlyBannedAsync(user.Id, _cancellationToken).Result)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you've been permanently banned from the kasino. Contact support for more information.", true);
                    return;
                }

                if (wagerCommand)
                {
                    // GetGamblerEntity will only return null if the user is permanbanned
                    // and we have a check further up the chain for that hence ignoring the null
                    var gambler = Money.GetGamblerEntityAsync(user.Id, ct: _cancellationToken).Result;
                    if (gambler != null)
                    {
                        var exclusion = Money.GetActiveExclusionAsync(gambler.Id, ct: _cancellationToken).Result;
                        if (exclusion != null)
                        {
                            _bot.SendChatMessage(
                                $"@{message.Author.Username}, you're self excluded from the kasino for another {(exclusion.Expires - DateTimeOffset.UtcNow).Humanize(precision: 3)}", true);
                            return;
                        }
                    }
                }
                
                if (user.UserRight < command.RequiredRight)
                {
                    _bot.SendChatMessage($"@{message.Author.Username}, you do not have access to use this command. Your rank: {user.UserRight.Humanize()}; Required rank: {command.RequiredRight.Humanize()}", true);
                    if (continueAfterProcess) continue;
                    break;
                }

                if (command.RateLimitOptions != null)
                {
                    var isRateLimited = RateLimitService.IsRateLimited(user, command, message.MessageRawHtmlDecoded);
                    if (isRateLimited.IsRateLimited)
                    {
                        _ = SendCooldownResponse(user, command.RateLimitOptions, isRateLimited.OldestEntryExpires!.Value, command.GetType().Name);
                        break;
                    }
                    RateLimitService.AddEntry(user, command, message.MessageRawHtmlDecoded);
                }
                _ = ProcessMessageAsync(command, message, user, match.Groups);
                if (!continueAfterProcess) break;
            }
        }
    }

    private async Task ProcessMessageAsync(ICommand command, MessageModel message, UserDbModel user, GroupCollection arguments)
    {
        var cts = new CancellationTokenSource(command.Timeout);
        var task = Task.Run(() => command.RunCommand(_bot, message, user, arguments, cts.Token), cts.Token);
        try
        {
            await task.WaitAsync(command.Timeout, cts.Token);
        }
        catch (OperationCanceledException e)
        {
            _logger.Error($"{command.GetType().Name} invoked by {user.KfUsername} timed out");
            _logger.Error(e);
            await _bot.SendChatMessageAsync(
                $"{user.FormatUsername()}, {command.GetType().Name} failed due to a timeout :(", true,
                autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        catch (Exception e)
        {
            _logger.Error($"{command.GetType().Name} invoked by {user.KfUsername} failed");
            _logger.Error(e);
            await _bot.SendChatMessageAsync(
                $"{user.FormatUsername()}, {command.GetType().Name} failed due to a retarded error :(", true,
                autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        if (task.IsFaulted)
        {
            _logger.Error($"{command.GetType().Name} invoked by {user.KfUsername} faulted");
            _logger.Error(task.Exception);
            await _bot.SendChatMessageAsync(
                $"{user.FormatUsername()}, {command.GetType().Name} failed due to shitty coding :(", true,
                autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        try
        {
            if (!(await SettingsProvider.GetValueAsync(BuiltIn.Keys.MoneyEnabled)).ToBoolean()) return;
            _logger.Debug("Money is enabled. Calculating VIP maybe?");
            var wagerCommand = HasAttribute<WagerCommand>(command);
            if (!wagerCommand) return;
            _logger.Debug("It's a wager command");
            var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: _cancellationToken);
            if (gambler == null) return;
            _logger.Debug($"Gambler ID is {gambler.Id}");
            if (gambler.TotalWagered < gambler.NextVipLevelWagerRequirement) return;
            _logger.Debug("They've met the wager requirement");
            // The reason for doing this instead of passing in TotalWagered is that otherwise VIP levels might
            // get skipped if the user is a low VIP level but wagering very large amounts
            var newLevel = Money.GetNextVipLevel(gambler.NextVipLevelWagerRequirement);
            if (newLevel == null)
            {
                _logger.Info("newLevel is null");
                return;
            }
            _logger.Info($"New level is {newLevel.VipLevel.Name} {newLevel.Tier}");
            var payout = await Money.UpgradeVipLevelAsync(gambler.Id, newLevel, _cancellationToken);
            _logger.Info($"Payout is {payout:N2}");
            await _bot.SendChatMessageAsync(
                $"🤑🤑 {user.FormatUsername()} has leveled up to to {newLevel.VipLevel.Icon} {newLevel.VipLevel.Name} Tier {newLevel.Tier} " +
                $"and received a bonus of {await payout.FormatKasinoCurrencyAsync()}", true);
            _logger.Info("Sent notification");
        }
        catch (Exception e)
        {
            _logger.Error(e);
        }
    }

    private async Task SendCooldownResponse(UserDbModel user, RateLimitOptionsModel options, DateTimeOffset oldestEntryExpires, string commandName)
    {
        if (options.Flags.HasFlag(RateLimitFlags.NoResponse))
        {
            _logger.Info("No response flag set. Ignoring");
            return;
        }
        _logger.Info($"Oldest entry: {oldestEntryExpires:o}");
        var timeRemaining = oldestEntryExpires - DateTimeOffset.UtcNow;
        TimeSpan? autoDeleteAfter = null;
        if (!options.Flags.HasFlag(RateLimitFlags.NoAutoDeleteCooldownResponse))
        {
            autoDeleteAfter = TimeSpan.FromMilliseconds(
                (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRateLimitCooldownAutoDeleteDelay)).ToType<int>());
        }

        await _bot.SendChatMessageAsync(
            $"{user.FormatUsername()}, please wait {timeRemaining.Humanize(maxUnit: TimeUnit.Minute, minUnit: TimeUnit.Millisecond, precision: 2)} before attempting to run {commandName} again.",
            true, autoDeleteAfter: autoDeleteAfter);
    }

    private async Task CleanupExpiredRateLimitEntriesTask()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            var interval = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRateLimitExpiredEntryCleanupInterval))
                .ToType<int>();
            await Task.Delay(TimeSpan.FromSeconds(interval), _cancellationToken);
            _logger.Info("Cleaning up expired rate limit entries");
            RateLimitService.CleanupExpiredEntries();
        }
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

/// <summary>
/// Use this on commands where the Regex should be tested even if there's no command prefix
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal class NoPrefixRequired : Attribute;