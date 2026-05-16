using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Commands.Kasino;

public class CecilCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?) (?<difficulty>\d+(?:\.\d+)?) (?<maxwin>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?) (?<difficulty>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex("^keno")
    ];
    
    public string? HelpText => "!cecil <bet> <optional difficulty> <optional max win>";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 5,
        Window = TimeSpan.FromSeconds(10)
    };
    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user,
        GroupCollection arguments,
        CancellationToken ctx)
    {
        if (message is { IsWhisper: false, MessageUuid: not null })
        {
            await botInstance.KfClient.DeleteMessageAsync(message.MessageUuid);
        }

        var cleanupDelay = TimeSpan.FromSeconds(15);
        
        if (!arguments.TryGetValue("bet", out var amount)) //if user just enters !keno
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !cecil <bet> <optional difficulty> <[i]optional max win > 1[/i] - Cecil Tool: https://i.ddos.lgbt/raw/CecilHelper.html>",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        var wager = Convert.ToDecimal(amount.Value);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        bool beta;
        double difficulty;

        double result;
        if (!arguments.TryGetValue("difficulty", out var diff))
        {
            difficulty = 1;
        }
        else
        {
            difficulty = Convert.ToDouble(diff.Value);
        }

        if (!arguments.TryGetValue("maxwin", out var maxWin))
        {
            GammaSkew skew = new GammaSkew(difficulty, 0);
            result = Cecil.Consult(skew, 0);
        }
        else
        {
            double mWin = Convert.ToDouble(maxWin.Value);
            BetaSkew skew = new BetaSkew(difficulty, mWin, 0);
            result = Cecil.Consult(skew, 0);
        }

        var payout = wager * Convert.ToDecimal(result);
        var net = payout - wager;
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager, net, WagerGame.Cecil);
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var red = $"{colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}";
        var green = $"{colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}";
        var color = (payout > wager) ? green : red;
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, Cecil has determined you are due [color={color}]{await payout.FormatKasinoCurrencyAsync()}[/color] from your wager of {await wager.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);

    }
}
