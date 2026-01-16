
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class CoinflipCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^coinflip$", RegexOptions.IgnoreCase),
        new Regex(@"^coinflip (?<amount>\d+) (?<choice>heads|tails)$", RegexOptions.IgnoreCase),
        new Regex(@"^coinflip (?<amount>\d+\.\d+) (?<choice>heads|tails)$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "!coinflip <amount> <heads|tails>, flip a coin";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(15)
    };
    private static double _houseEdge = 0.015; // house edge hack?

    public async Task RunCommand(ChatBot botInstance, MessageModel messagen, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay, BuiltIn.Keys.KasinoCoinflipCleanupDelay,
            BuiltIn.Keys.KasinoCoinflipEnabled
        ]);

        var coinflipEnabled = settings[BuiltIn.Keys.KasinoCoinflipEnabled].ToBoolean();
        if (!coinflipEnabled)
        {
            var gameDisabledCleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, coinflip is currently disabled.",
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }

        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoCoinflipCleanupDelay].ToType<int>());

        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !coinflip <wager> <heads|tails>",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        if (!arguments.TryGetValue("choice", out var choice))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !coinflip <wager> <heads|tails>",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var choiceStr = choice.Value.ToLowerInvariant();
        var wager = Convert.ToDecimal(amount.Value);
        if (wager <= 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your wager must be greater than zero.",
                true, autoDeleteAfter: cleanupDelay);
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
        var rolled = Money.GetRandomDouble(gambler);
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);


        decimal newBalance;
        if (rolled > 0.5 + _houseEdge)
        {
            // won
            var coinflipAnimation = await GetCoinFlipAnimationUrl(choiceStr);

            await botInstance.SendChatMessageAsync($"[IMG]{coinflipAnimation}[/IMG]", true, autoDeleteAfter: cleanupDelay);
            await Task.Delay(1200, ctx);

            var effect = wager;
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, effect, WagerGame.CoinFlip, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]WON![/COLOR][/B] " +
                $"You won {await effect.FormatKasinoCurrencyAsync()} and your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
        }
        else
        {
            // lost
            bool isJacky = rolled > 0.5; // would've won without house edge
            var coinflipAnimationURL = await GetCoinFlipAnimationUrl("heads" == choiceStr ? "tails" : "heads", isJacky);

            await botInstance.SendChatMessageAsync($"[IMG]{coinflipAnimationURL}[/IMG]", true, autoDeleteAfter: cleanupDelay);
            await Task.Delay(1200, ctx);

            newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.CoinFlip, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]LOST![/COLOR][/B] " +
                $"Your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
        }
    }

    private static async Task<string?> GetCoinFlipAnimationUrl(string choiceStr, bool isJacky = false)
    {
        string animationPath;
        if (isJacky)
        {
            animationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", $"bossmancoin-{choiceStr}-jacky.webp");
        }
        else
        {
            animationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", $"bossmancoin-{choiceStr}.webp");
        }
        if (!File.Exists(animationPath)) throw new DirectoryNotFoundException($"Coinflip animation missing at {animationPath}");

        using var imageStream = File.OpenRead(animationPath);
        return await Zipline.Upload(imageStream, new MediaTypeHeaderValue("image/webp"), "1h");
    }
}