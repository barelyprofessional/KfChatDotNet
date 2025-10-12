using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot.Commands;

[KasinoCommand]
public class GetBalanceCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^balance", RegexOptions.IgnoreCase),
        new Regex("^bal$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Get your gamba balance";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, your balance is {await gambler!.Balance.FormatKasinoCurrencyAsync()}", true);
    }
}

[KasinoCommand]
public class GetExclusionCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^exclusion$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "Get your exclusion status";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving {user.Id}'s gambler entity");
        }
        var exclusion = await Money.GetActiveExclusionAsync(gambler.Id, ct: ctx);
        if (exclusion == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you are currently not excluded.", true);
            return;
        }

        var duration =
            (exclusion.Expires - exclusion.Created).Humanize(precision: 1, minUnit: TimeUnit.Second,
                maxUnit: TimeUnit.Day);
        var expires =
            (exclusion.Expires - DateTimeOffset.UtcNow).Humanize(precision: 2, minUnit: TimeUnit.Second,
                maxUnit: TimeUnit.Day);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, your exclusion for {duration} expires in {expires}", true);
    }
}

[KasinoCommand]
public class SendJuiceCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^juice (?<user_id>\d+) (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^juice (?<user_id>\d+) (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Send juice to somebody";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == int.Parse(arguments["user_id"].Value), ctx);
        var amount = decimal.Parse(arguments["amount"].Value);
        if (gambler == null)
        {
            logger.Error($"Caught a null when looking up {user.KfUsername}");
            return;
        }
        if (gambler.Balance < amount)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you don't have enough money to juice this much.", true);
            return;
        }

        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, the user ID you gave doesn't exist.", true);
            return;
        }

        var targetGambler = await Money.GetGamblerEntityAsync(targetUser.Id, ct: ctx);
        if (targetGambler == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't juice a banned user", true);
            return;
        }

        await Money.ModifyBalanceAsync(gambler.Id, -amount, TransactionSourceEventType.Juicer,
            $"Juice sent to {targetUser.KfUsername}", ct: ctx);
        await Money.ModifyBalanceAsync(targetGambler.Id, amount, TransactionSourceEventType.Juicer, $"Juice from {user.KfUsername}",
            gambler.Id, ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, {await amount.FormatKasinoCurrencyAsync()} has been sent to {targetUser.KfUsername}", true);
    }
}

[KasinoCommand]
public class RakebackCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^rakeback", RegexOptions.IgnoreCase),
        new Regex(@"^rapeback", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Collect your rakeback";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(30)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving {user.Id}'s gambler entity");
        }
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.MoneyRakebackPercentage, BuiltIn.Keys.MoneyRakebackMinimumAmount
        ]);
        var mostRecentRakeback = await db.Transactions.OrderBy(x => x.Id).LastOrDefaultAsync(tx =>
            tx.EventSource == TransactionSourceEventType.Rakeback && tx.Gambler.Id == gambler.Id, cancellationToken: ctx);
        long offset = 0;
        if (mostRecentRakeback != null)
        {
            offset = mostRecentRakeback.TimeUnixEpochSeconds;
        }

        var wagers = await db.Wagers.Where(w => w.Gambler.Id == gambler.Id && w.TimeUnixEpochSeconds > offset).ToListAsync(ctx);
        if (wagers.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you haven't wagered since your last rakeback.", true);
            return;
        }

        var wagered = wagers.Sum(w => w.WagerAmount);
        var rakeback = wagered * (decimal)(settings[BuiltIn.Keys.MoneyRakebackPercentage].ToType<float>() / 100.0);
        var minimumRakeback = settings[BuiltIn.Keys.MoneyRakebackMinimumAmount].ToType<decimal>();
        if (rakeback < minimumRakeback)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your rakeback payout of {await rakeback.FormatKasinoCurrencyAsync()} is below the minimum amount of {await minimumRakeback.FormatKasinoCurrencyAsync()}", true);
            return;
        }
        await Money.ModifyBalanceAsync(gambler.Id, rakeback, TransactionSourceEventType.Rakeback, "Rakeback claimed by gambler",
            ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, the hostess has given you {await rakeback.FormatKasinoCurrencyAsync()} rakeback", true);
    }
}

[KasinoCommand]
public class LossbackCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^lossback", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Collect your lossback";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        Window = TimeSpan.FromSeconds(30),
        MaxInvocations = 1
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving {user.Id}'s gambler entity");
        }
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.MoneyLossbackPercentage, BuiltIn.Keys.MoneyLossbackMinimumAmount
        ]);
        var mostRecentLossback = await db.Transactions.OrderBy(x => x.Id).LastOrDefaultAsync(tx =>
            tx.EventSource == TransactionSourceEventType.Lossback && tx.Gambler.Id == gambler.Id, cancellationToken: ctx);
        long offset = 0;
        if (mostRecentLossback != null)
        {
            offset = mostRecentLossback.TimeUnixEpochSeconds;
        }
        logger.Info($"{user.KfUsername}'s offset is {offset}");

        var wagers = await db.Wagers.Where(w => w.Gambler.Id == gambler.Id && w.TimeUnixEpochSeconds > offset && w.Multiplier < 1).ToListAsync(ctx);
        if (wagers.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have any losses to juice back.", true);
            return;
        }
        logger.Info($"{user.KfUsername} has {wagers.Count} wagers to lossback");

        var wagered = wagers.Sum(wager => Math.Abs(wager.WagerEffect));
        var lossback = wagered * (decimal)(settings[BuiltIn.Keys.MoneyLossbackPercentage].ToType<float>() / 100.0);
        var minimumLossback = settings[BuiltIn.Keys.MoneyLossbackMinimumAmount].ToType<decimal>();
        if (lossback < minimumLossback)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your lossback payout of {await lossback.FormatKasinoCurrencyAsync()} is below the minimum amount of {await minimumLossback.FormatKasinoCurrencyAsync()}", true);
            return;
        }
        await Money.ModifyBalanceAsync(gambler.Id, lossback, TransactionSourceEventType.Lossback, "Lossback claimed by gambler",
            ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, the hostess has given you {await lossback.FormatKasinoCurrencyAsync()} lossback", true);
    }
}

[KasinoCommand]
public class AbandonKasinoCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^abandon$", RegexOptions.IgnoreCase),
        new Regex(@"^abandon confirm$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Abandon your Keno Kasino gambler account";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (!message.MessageRawHtmlDecoded.EndsWith("abandon confirm"))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, are you sure you wish to abandon your Keno Kasino™ account?[br]" +
                                                   $"This will reset your wager statistics, balance, and temporary exclusions.[br]" +
                                                   $"You will lose all perks such as VIP levels and custom titles.[br]" +
                                                   $"To confirm, reply with: !abandon confirm", true);
            return;
        }
        await using var db = new ApplicationDbContext();
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving {user.Id}'s gambler entity");
        }
        db.Attach(gambler);
        gambler!.State = GamblerState.Abandoned;
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, Kasino account with ID {gambler.Id} has been marked as abandoned.", true);
    }
}