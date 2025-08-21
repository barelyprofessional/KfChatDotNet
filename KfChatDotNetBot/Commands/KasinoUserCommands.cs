using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

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

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var gambler = await user.GetGamblerEntity(ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"@{user.KfUsername}, your balance is {await gambler!.Balance.FormatKasinoCurrencyAsync()}", true);
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

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var gambler = await user.GetGamblerEntity(ct: ctx);
        var exclusion = await gambler!.GetActiveExclusion(ct: ctx);
        if (exclusion == null)
        {
            await botInstance.SendChatMessageAsync($"@{user.KfUsername}, you are currently not excluded.", true);
            return;
        }

        var duration =
            (exclusion.Expires - exclusion.Created).Humanize(precision: 1, minUnit: TimeUnit.Second,
                maxUnit: TimeUnit.Day);
        var expires =
            (exclusion.Expires - DateTimeOffset.UtcNow).Humanize(precision: 2, minUnit: TimeUnit.Second,
                maxUnit: TimeUnit.Day);
        await botInstance.SendChatMessageAsync(
            $"@{user.KfUsername}, your exclusion for {duration} expires in {expires}", true);
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

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var gambler = await user.GetGamblerEntity(ct: ctx);
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == int.Parse(arguments["user_id"].Value), ctx);
        var amount = decimal.Parse(arguments["amount"].Value);
        if (gambler!.Balance < amount)
        {
            await botInstance.SendChatMessageAsync($"@{user.KfUsername}, you don't have enough money to juice this much.", true);
            return;
        }

        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"@{user.KfUsername}, the user ID you gave doesn't exist.", true);
            return;
        }

        var targetGambler = await targetUser.GetGamblerEntity(ct: ctx);
        if (targetGambler == null)
        {
            await botInstance.SendChatMessageAsync($"@{user.KfUsername}, you can't juice a banned user", true);
            return;
        }

        await gambler.ModifyBalance(-amount, TransactionSourceEventType.Juicer,
            $"Juice sent to {targetUser.KfUsername}", ct: ctx);
        await targetGambler.ModifyBalance(amount, TransactionSourceEventType.Juicer, $"Juice from {user.KfUsername}",
            gambler, ctx);
        await botInstance.SendChatMessageAsync($"@{user.KfUsername}, {await amount.FormatKasinoCurrencyAsync()} has been sent to {targetUser.KfUsername}", true);
    }
}