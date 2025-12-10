using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class GuessWhatNumberCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^guess (?<amount>\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^guess (?<amount>\d+\.\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
        new Regex("^guess$")
    ];
    public string? HelpText => "What number am I thinking of?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromMilliseconds((await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoGuessWhatNumberCleanupDelay)).ToType<int>());
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !guess <wager> <number between 1 and 10>", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var wager = Convert.ToDecimal(amount.Value);
        var guess = Convert.ToInt32(arguments["number"].Value);
        if (guess is < 1 or > 10)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your guess must be between 1 and 10", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var answer = Money.GetRandomNumber(gambler, 1, 10);
        decimal newBalance;
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        if (guess == answer)
        {
            var effect = wager * 9;
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, effect, WagerGame.GuessWhatNumber, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]correct![/color] You won {await effect.FormatKasinoCurrencyAsync()} and your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.GuessWhatNumber, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]wrong![/color] I was thinking of {answer}. Your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
    }
}