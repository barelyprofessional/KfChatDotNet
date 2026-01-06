using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using NLog;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Commands.Kasino;

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
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        Window = TimeSpan.FromSeconds(60),
        MaxInvocations = 1
    };

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

[KasinoCommand]
public class PocketWatchCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^pocketwatch (?<user_id>\d+)", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "Check a user's balance";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == int.Parse(arguments["user_id"].Value), ctx);
        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, the user ID you gave doesn't exist.", true);
            return;
        }

        var targetGambler = await Money.GetGamblerEntityAsync(targetUser.Id, ct: ctx);
        if (targetGambler == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, this user is excluded from the kasino", true);
            return;
        }
        
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, {targetUser.KfUsername} has {await targetGambler.Balance.FormatKasinoCurrencyAsync()}", true);
    }
}

[KasinoCommand]
[WagerCommand]
public class HostessCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^hostess", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "Ask the hostess for help";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(30)
    };

    private static readonly string[] StaticResponses = [
        "For questions regarding your current contract please contact us at contact@bossmanjack.com",
        "Unspecified error",
        "Have you considered giving us a review on TrustPilot?",
        "We are sincerely sorry to hear that you are not having a positive experience on our platform. Please be assured that we take matters of fairness and transparency very seriously.",
        "At the Kasino, we prioritize strict adherence to regulatory requirements to maintain the security and integrity of our platform.",
        "There are currently no hosts online to serve your request.",
        "We would like to assist you further and understand better the issue. Due to that, we have requested further information.",
        "When it comes to RTP, it is important to understand that this number is calculated based on at least 1 million bets. So, over a session of a few thousand bets, anything can happen, which is exactly what makes gambling exciting.",
        "We understand that gambling involves risks, and while some players may experience periods of winning and losing, we strive to provide resources and tools to support responsible gambling practices.",
        "Thank you for taking the time to leave a 5-star review! We're thrilled to have provided you with a great experience.",
        "Please rest assured that our platform operates with certified random number generators to ensure fairness and transparency in all gaming outcomes. We do not manipulate the odds or monitor games to favor any particular outcome.",
        "We would like to inform you that we have responded to your recent post.",
        "All of our Kasino originals are 100% probably fair and each and every single bet placed at our any games are verifiable.",
        "We want to emphasize that our games are developed with the highest standards of integrity and fairness.",
        "Stop harrassing me",
    ];

    private static readonly string[] LlmPrompts = [
        "You are a hostess for a virtual casino. You've just gotten a message from a customer with cripling gambling addiction issues. Respond in a smug and condescending manner.",
        "You are an overworked fastfood worker at a drive-thru. A confused gambling addict just arrived. Respond with at most two sentences."
    ];

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var random = RandomShim.Create(StandardRng.Create());

        if (random.NextDouble() < 0.06)
        {
            // ignore 6% of requests like the old hostess command
            return;
        }

        var orKeySet = await SettingsProvider.GetValueAsync(BuiltIn.Keys.OpenrouterApiKey);
        if (orKeySet.Value == null || random.NextDouble() < 0.3)
        {
            var response = StaticResponses[random.Next(0, StaticResponses.Length)];
            await botInstance.SendChatMessageAsync(response, true);
            return;
        }

        var msg = message.MessageRaw.Replace("hostess", "").Trim();
        if (string.IsNullOrWhiteSpace(msg))
        {
            msg = "I need help with my gambling addiction.";
        }

        var llmResponse = await OpenRouter.GetResponseAsync(
            LlmPrompts[random.Next(0, LlmPrompts.Length)],
            msg,
            model: "deepseek/deepseek-v3.2",
            Temperature: 1.0f + (float)((random.NextDouble() - 0.3) * 0.5)
        );
        if (llmResponse == null)
        {
            var fallback = StaticResponses[random.Next(0, StaticResponses.Length)];
            await botInstance.SendChatMessageAsync(fallback, true);
            return;
        }

        await botInstance.SendChatMessageAsync(llmResponse, true, ChatBot.LengthLimitBehavior.TruncateExactly);
    }
}

[KasinoCommand]
public class GetDailyDollarCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^daily", RegexOptions.IgnoreCase),
        new Regex("^juiceme", RegexOptions.IgnoreCase),

    ];
    public string? HelpText => "Get your daily dollah";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoDailyDollarEnabled, BuiltIn.Keys.KasinoDailyDollarAmount
        ]);
        if (!settings[BuiltIn.Keys.KasinoDailyDollarEnabled].ToBoolean())
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, daily dollar has been disabled :(", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler!.Created.Date == DateTime.UtcNow.Date)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, new accounts cannot redeem a daily dollar", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }
        await using var db = new ApplicationDbContext();
        var mostRecentTxn = await db.Transactions.OrderBy(x => x.Id).LastOrDefaultAsync(x =>
            x.Gambler == gambler && x.EventSource == TransactionSourceEventType.DailyDollar, cancellationToken: ctx);
        if (mostRecentTxn != null)
        {
            var rolloverTime = await Money.GetKasinoDate();
            // It's really more a question of whether the most recent txn was in the same game day
            if (mostRecentTxn.Time >= rolloverTime)
            {
                var span = rolloverTime.AddDays(1) - DateTimeOffset.UtcNow;
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, your next daily dollar will be available in {span.Humanize(maxUnit: TimeUnit.Hour, minUnit: TimeUnit.Second)}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(15));
                return;
            }
        }

        var amount = settings[BuiltIn.Keys.KasinoDailyDollarAmount].ToType<decimal>();
        await Money.ModifyBalanceAsync(gambler!.Id, amount, TransactionSourceEventType.DailyDollar,
            "Daily dollar redemption", ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you redeemed {await amount.FormatKasinoCurrencyAsync()}", true,
            autoDeleteAfter: TimeSpan.FromSeconds(15));
    }
}