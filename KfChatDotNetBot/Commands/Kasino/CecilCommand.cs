using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class CecilCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?) (?<difficulty>\d+(?:\.\d+)?) (?<maxwin>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?) (?<difficulty>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex(@"^cecil (?<bet>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase),
        new Regex("^cecil")
    ];
    
    public string? HelpText => "!cecil <bet> <optional difficulty> <optional max win>";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => new()
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
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.KasinoCecilEnabled]);
        
        var cecilEnabled = settings[BuiltIn.Keys.KasinoCecilEnabled].ToBoolean();
        if (!cecilEnabled)
        {
            await botInstance.ReplyToUser(message, 
                $"{user.FormatUsername()}, Cecil is currently disabled.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        if (!arguments.TryGetValue("bet", out var amount)) //if user just enters !keno
        {
            await botInstance.ReplyToUser(message,
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
            await botInstance.ReplyToUser(message,
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        var difficulty = 1.0;

        double result;
        if (arguments.TryGetValue("difficulty", out var diff))
        {
            difficulty = Convert.ToDouble(diff.Value);
        }

        if (!arguments.TryGetValue("maxwin", out var maxWin))
        {
            var skew = new GammaSkew(difficulty, 0);
            result = Cecil.Consult(skew, 0);
        }
        else
        {
            var mWin = Convert.ToDouble(maxWin.Value);
            if (mWin < 1)
            {
                await botInstance.ReplyToUser(message, $"{user.FormatUsername()}, max win must be greater than 1.", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            var skew = new BetaSkew(difficulty, mWin, 0);
            result = Cecil.Consult(skew);
        }

        var payout = wager * Convert.ToDecimal(result);
        var net = payout - wager;
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager, net, WagerGame.Cecil, ct: ctx);
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var red = colors[BuiltIn.Keys.KiwiFarmsRedColor].Value;
        var green = colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
        var color = (payout > wager) ? green : red;
        await botInstance.ReplyToUser(message, 
            $"{user.FormatUsername()}, Cecil has determined you are due [color={color}]{await payout.FormatKasinoCurrencyAsync()}[/color] from your wager of {await wager.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);

    }
}
