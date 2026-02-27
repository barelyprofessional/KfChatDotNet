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
public class LimboCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^limbo (?<amount>\d+(?:\.\d+)?) (?<number>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^limbo (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex("^limbo")
    ];
    public string? HelpText => "!limbo <bet amount> <optional number, default 2>";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 10,
        Window = TimeSpan.FromSeconds(30)
    };

    private const double Min = 1;
    private const double Max = 10000;
    private decimal HOUSE_EDGE = (decimal)0.98;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        decimal limboNumber; //user number
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay,
            BuiltIn.Keys.KasinoLimboCleanupDelay, BuiltIn.Keys.KasinoLimboEnabled, 
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        
        // Check if limbo is enabled
        var limboEnabled = (settings[BuiltIn.Keys.KasinoLimboEnabled]).ToBoolean();
        if (!limboEnabled)
        {
            var gameDisabledCleanupDelay= TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, limbo is currently disabled.", 
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
        
        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoLimboCleanupDelay].ToType<int>());
        
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !limbo <wager>",
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
        
        if (wager == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you have to wager more than {await wager.FormatKasinoCurrencyAsync()}", true,
                autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (!arguments.TryGetValue("number", out var number))
        {
            limboNumber = 2;
            //set user number to 2 if they didn't enter anything
        }
        else limboNumber = Convert.ToDecimal(number.Value);

        if (limboNumber <= 1)
        {
            //cancel the game if user does not choose a correct number
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you must choose a number greater than 1", true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }
        decimal newBalance;
        var casinoNumbers = Get1XWeightedRandomNumber(Min, (double)(limboNumber * limboNumber), limboNumber);
        string colorToUse;
        if (casinoNumbers[0] >= limboNumber)
        {
            //you win
            colorToUse = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value!;
            var win = wager * limboNumber - wager;
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, win, WagerGame.Limbo, ct: ctx);
            await botInstance.SendChatMessageAsync($"[b][color={colorToUse}] {casinoNumbers[1]:N2}[/color][/b][br]{user.FormatUsername()}, you " +
                                                   $"[color={settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value}] won {await win.FormatKasinoCurrencyAsync()}![/color] " +
                                                   $"Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}!", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        if (limboNumber / 2 > casinoNumbers[1]) colorToUse = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value!; //use red for the number if you're not close
        else if (limboNumber *3 / 4 > casinoNumbers[1])
            colorToUse = "yellow"; //use yellow for the number if you're pretty close
        else colorToUse = "orange"; //use orange for mid range guess
        //you lose
        newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Limbo, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"[b][color={colorToUse}] {casinoNumbers[1]:N2}[/color][/b][br]{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsRedColor].Value}]" +
            $"lost {await wager.FormatKasinoCurrencyAsync()}[/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}.",
            true, autoDeleteAfter: cleanupDelay);

    }
    
    //returns a distribution with a 1/multi chance of getting a number below or above sqr(min * max) (so max should basically be multi^2). basically gives you a 1/x fair chance to win
    //then scales the number using the number scaling function
    private decimal[] Get1XWeightedRandomNumber(double minValue, double maxValue, decimal multi)
    {
        var random = RandomShim.Create(StandardRng.Create());
        var skew = 1.0 / (double)(multi);
        var gamma = Math.Log(0.5) / Math.Log(skew);
        var r = random.NextDouble();
        var rP = 1 - Math.Pow(1 - r, gamma);
        var lnMin = Math.Log(minValue);
        var lnMax = Math.Log(maxValue);
        var exponent = lnMin + rP * (lnMax - lnMin);
        var result = new decimal[2];
        result[0] = (decimal)Math.Exp(exponent) * HOUSE_EDGE;
        result[1] = GetScaledNumber(lnMin, lnMax, exponent, result[0], multi);
        return result;
    }

    private static decimal GetScaledNumber(double lnMin, double lnMax, double exponent, decimal result, decimal multi)
    {
        var anchor = Math.Log((double)multi);
        var deltaMax = lnMax - anchor;
        var k = Math.Log(Max / (double)(multi * multi)) / deltaMax;
        var delta = exponent - anchor;
        var logFactor = k * delta;
        var factor = Math.Exp(logFactor);
        var preResult = (result * (decimal)factor);
        
        if (!((double)preResult < anchor)) return preResult;
        
        var minTheo = (double)(multi * multi) / Max;
        var logMinTheo = Math.Log(minTheo);
        var logPreResult = Math.Log((double)preResult);
        var fraction = (logPreResult - logMinTheo) / (anchor - logMinTheo);
        return (decimal)Math.Exp((anchor * fraction));
    }
}

