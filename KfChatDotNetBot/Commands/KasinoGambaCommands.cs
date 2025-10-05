using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

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
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !guess <wager> <number between 1 and 10>", true);
            return;
        }

        var wager = Convert.ToDecimal(amount);
        var guess = Convert.ToInt32(arguments["number"].Value);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true);
            return;
        }

        var answer = Money.GetRandomNumber(gambler, 1, 10);
        if (guess == answer)
        {
            var effect = wager * 9;
            await Money.NewWagerAsync(gambler.Id, wager, effect, WagerGame.GuessWhatNumber, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, correct! You won {await effect.FormatKasinoCurrencyAsync()} and your balance is now {await (gambler.Balance + effect).FormatKasinoCurrencyAsync()}",
                true);
            return;
        }

        await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.GuessWhatNumber, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, wrong! I was thinking of {answer}. Your balance is now {await (gambler.Balance - wager).FormatKasinoCurrencyAsync()}",
            true);
    }
}