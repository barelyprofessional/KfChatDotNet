using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

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
    public UserRight RequiredRight => UserRight.Loser;
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

        var wager = Convert.ToDecimal(amount.Value);
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
        decimal newBalance;
        if (guess == answer)
        {
            var effect = wager * 9;
            await Money.NewWagerAsync(gambler.Id, wager, effect, WagerGame.GuessWhatNumber, ct: ctx);
            newBalance = gambler.Balance + effect;
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, correct! You won {await effect.FormatKasinoCurrencyAsync()} and your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
                true);
            return;
        }

        await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.GuessWhatNumber, ct: ctx);
        newBalance = gambler.Balance - wager;
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, wrong! I was thinking of {answer}. Your balance is now {await newBalance.FormatKasinoCurrencyAsync()}",
            true);
    }
}

[KasinoCommand]
[WagerCommand]
public class KenoCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^keno (?<amount>\d+) (?<numbers>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^keno (?<amount>\d+\.\d+) (?<numbers>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^keno (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^keno (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^keno$")
    ];
    public string? HelpText => "!keno [bet amount] [numbers to pick(optional, default 10)]";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(10)
    };

    private const string PlayerNumberDisplay = "⬜";
    private const string CasinoNumberDisplay = "🔶";
    private const string MatchRevealDisplay = "💠";
    private const string BlankSpaceDisplay = "⬛";


    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (!arguments.TryGetValue("amount", out var amount)) //if user just enters !keno
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !keno <wager> <number between 1 and 10>, or !keno <wager> and 10 will be selected automatically", true);
            return;
        }
        var wager = Convert.ToDecimal(amount.Value);
        var numbers = !arguments.TryGetValue("numbers", out var userNumbers) ? 10 : Convert.ToInt32(userNumbers.Value); //if user just enters !keno <wager>
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
        
        if (numbers is < 1 or > 10) //if user picks invalid numbers
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can only pick numbers from 1 - 10", true);
            return;
        }
        
        var payoutMultipliers = new[,]//stole the payout multis from stake keno and re added the RTP, except for the 1000x
        {
            {0.0, 4.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0},               // 1 selection
            {0.0, 0.0, 17.27, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0},             // 2 selections
            {0.0, 0.0, 0.0, 82.32, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0},             // 3 selections
            {0.0, 0.0, 0.0, 10.1, 261.61, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0},           // 4 selections
            {0.0, 0.0, 0.0, 4.5, 48.48, 454.54, 0.0, 0.0, 0.0, 0.0, 0.0},          // 5 selections
            {0.0, 0.0, 0.0, 0.0, 11.11, 353.53, 717.17, 0.0, 0.0, 0.0, 0.0},       // 6 selections
            {0.0, 0.0, 0.0, 0.0, 7.07, 90.90, 404.04, 808.08, 0.0, 0.0, 0.0},      // 7 selections
            {0.0, 0.0, 0.0, 0.0, 5.05, 20.20, 272.72, 606.06, 909.09, 0.0, 0.0},   // 8 selections
            {0.0, 0.0, 0.0, 0.0, 4.04, 11.11, 56.56, 505.05, 808.08, 1000.0, 0.0}, // 9 selections
            {0.0, 0.0, 0.0, 0.0, 3.53, 8.08, 13.13, 63.63, 505.05, 808.08, 1000.0} // 10 selections
        };
        var playerNumbers = GenerateKenoNumbers(numbers, gambler);
        var casinoNumbers = GenerateKenoNumbers(10, gambler);
        var matches = playerNumbers.Intersect(casinoNumbers).ToList();
        var payoutMulti = payoutMultipliers[numbers - 1, matches.Count];
        
        await AnimatedDisplayTable(playerNumbers, casinoNumbers, matches, botInstance);
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        
        if (payoutMulti == 0) //you lose
        {
            await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Keno, ct: ctx);
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]lost {await wager.FormatKasinoCurrencyAsync()}[/color]. Your balance is now: {await gambler.Balance.FormatKasinoCurrencyAsync()}.", true);
            return;
        }

        //you win
        var win = wager * (decimal)payoutMulti;
        // Required to avoid compiler errors when trying to format it in the win message
        var newBalance = gambler.Balance + win;
        await Money.NewWagerAsync(gambler.Id, wager, wager * (decimal)payoutMulti, WagerGame.Keno, ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]won {await win.FormatKasinoCurrencyAsync()} with a {payoutMulti}x multi![/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}.", true);
    }
    
    private async Task AnimatedDisplayTable(List<int> playerNumbers, List<int> casinoNumbers, List<int> matches, ChatBot botInstance)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var displayMessage = "";
        //keno board is 8 x 5, numbers left to right, top to bottom
        //FIRST FRAME 11111111111111111111111111111
        var totalCounter = 1;
        for (var column = 0; column < 5; column++)
        {
            for (var row = 0; row < 8; row++)
            {
                if (playerNumbers.Contains(totalCounter)) displayMessage += PlayerNumberDisplay;
                else displayMessage += BlankSpaceDisplay;
                totalCounter++;
            }
            displayMessage += "[br]";
        }

        var msg = await botInstance.SendChatMessageAsync(displayMessage, true);
        var i = 0;
        while (msg.ChatMessageId == null)
        {
            i++;
            if (msg.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost) return;
            if (i > 60) return;
            await Task.Delay(100);
        }

        var frameDelay = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoKenoFrameDelay)).ToType<int>();
        //FIRST FRAME 11111111111111111111111111111
        for (var frame = 0; frame < 10; frame++) //1 frame per casino number
        {
            displayMessage = "";
            totalCounter = 1;
            for (var column = 0; column < 5; column++)
            {
                for (var row = 0; row < 8; row++)
                {
                    if (casinoNumbers.Take(frame+1).Contains(totalCounter))
                    {
                       
                        if (matches.Contains(totalCounter))
                        {
                            displayMessage += MatchRevealDisplay;
                        }
                        else
                        {
                            displayMessage += CasinoNumberDisplay;
                        }
                    }
                    else if (playerNumbers.Contains(totalCounter)) displayMessage += PlayerNumberDisplay;
                    else displayMessage += BlankSpaceDisplay;

                    totalCounter++;
                }
                displayMessage += "[br]";
            }
            await botInstance.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, displayMessage);
            await Task.Delay(frameDelay);
            if (displayMessage.Length <= 79 && displayMessage.Contains(BlankSpaceDisplay) &&
                (displayMessage.Contains(CasinoNumberDisplay) || displayMessage.Contains(MatchRevealDisplay) ||
                 frame == 9)) continue; //every board should have blank spaces and casino numbers or matches. player numbers might be hidden by matches
            logger.Info($"Casino numbers: {string.Join(",", casinoNumbers)} | Player Numbers: {string.Join(",", playerNumbers)} | Matches: {string.Join(",", matches)} | Frame: {frame - 1} | Display Board:");
            logger.Info(displayMessage);
            await botInstance.SendChatMessageAsync($"Keno is bugged dewd, died on frame {frame} :bossman:", true);
        }
    }

    private List<int> GenerateKenoNumbers(int size, GamblerDbModel gambler)
    {
        var numbers = new List<int>();
        for (var i = 0; i < size; i++)
        {
            var repeatNum = true;
            while (repeatNum)
            {
                var randomNum = Money.GetRandomNumber(gambler, 1, 40);
                if (numbers.Contains(randomNum)) continue;
                numbers.Add(randomNum);
                repeatNum = false;
            }
        }

        return numbers;
    }
    
}