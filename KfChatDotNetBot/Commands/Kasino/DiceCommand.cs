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
public class DiceCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"dice (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^dice (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!dice, roll the dice (not really, you roll between 0 - 100)";
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
        var cleanupDelay = TimeSpan.FromMilliseconds((await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoKenoCleanupDelay)).ToType<int>());
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !dice <wager>",
                true, autoDeleteAfter: cleanupDelay);
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
            return;
        }
        var rolled = Money.GetRandomDouble(gambler);
        decimal newBalance;
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        // print dice game slider
        await botInstance.SendChatMessageAsync($"{ConstructDiceGameOutput(rolled)}",true, autoDeleteAfter: cleanupDelay);
        if (rolled > 0.5 + _houseEdge)
        {
            // you win dice
            var effect = wager;
            await Money.NewWagerAsync(gambler.Id, wager, effect, WagerGame.Dice, ct: ctx);
            newBalance = gambler.Balance + effect;
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you rolled a {rolled * 100:N2} and [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]WON![/COLOR][/B] " +
                $"You won {await effect.FormatKasinoCurrencyAsync()} and your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
        }
        else
        {
            // you lose dice
            await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Dice, ct: ctx);
            newBalance = gambler.Balance - wager;
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you rolled a {rolled * 100:N2} and [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]LOST![/COLOR][/B] " +
                $"Your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
        }
    }

    private static string ConstructDiceGameOutput(double rolled)
    {
        // returns two rows as one string
        // row one has dice emoji shifted usisng spaces by appropriate ammount accourding to rolled
        // second row constructs the dice "meter" according to game specs
        var diceEmoji = "🎲";
        var invisibleAsciiSpace = "⠀";  //U+2800
        // 21 asciispaces fills the display, 20 dashes and one | fills the meter
        // rolled * 21 * invisible ascii space creates the illusion of dice being alligned with rolled number
        var toShift = (int)Math.Round(21 * rolled);
        string diceDisplayShifted = String.Concat(Enumerable.Repeat(invisibleAsciiSpace, toShift));
        diceDisplayShifted += diceEmoji;

        const int DICE_METER_LENGTH = 20; // uhh this should influence how much the dice emoji is shifted as declared before just leave at 20 for now
        const string DICE_METER_LEFT = "─";
        const string DICE_METER_LEFT_COLOR = "#f1323e"; // red for lose
        const string DICE_METER_MIDDLE = "┃";
        const string DICE_METER_MIDDLE_COLOR = "#886cff"; // I forgot what this color is
        const string DICE_METER_RIGHT = "─";
        const string DICE_METER_RIGHT_COLOR = "#3dd179"; // green for win

        string diceMeter = "";

        if (rolled > 0.5 && rolled < 0.5 + _houseEdge)
        {
            // rigged dice scenario
            diceMeter += $"[B][COLOR={DICE_METER_LEFT_COLOR}]";
            int redMeterLength = Math.Min((int)Math.Round(DICE_METER_LENGTH * rolled + 1), DICE_METER_LENGTH);
            diceMeter += String.Concat(Enumerable.Repeat(DICE_METER_LEFT, redMeterLength));
            diceMeter += $"[/COLOR][COLOR={DICE_METER_MIDDLE_COLOR}]{DICE_METER_MIDDLE}[/COLOR][COLOR={DICE_METER_RIGHT_COLOR}]";
            diceMeter += String.Concat(Enumerable.Repeat(DICE_METER_RIGHT, DICE_METER_LENGTH - (diceMeter.Split(DICE_METER_LEFT).Length - 1))) + "[/COLOR][/B]  [img]https://i.ddos.lgbt/u/Nq3JXD.webp[/img]";
        }
        else
        {
            // no rig scenario
            diceMeter += $"[B][COLOR={DICE_METER_LEFT_COLOR}]";
            diceMeter += String.Concat(Enumerable.Repeat(DICE_METER_LEFT, DICE_METER_LENGTH / 2)); // ---------
            diceMeter += "[/COLOR]";
            diceMeter += $"[COLOR={DICE_METER_MIDDLE_COLOR}]{DICE_METER_MIDDLE}[/COLOR]"; // |
            diceMeter += $"[COLOR={DICE_METER_RIGHT_COLOR}]";
            diceMeter += String.Concat(Enumerable.Repeat(DICE_METER_RIGHT, DICE_METER_LENGTH / 2)); // --------
            diceMeter += "[/COLOR][/B]";
            
        }
        return $"{diceDisplayShifted}\n{diceMeter}";
    }
}