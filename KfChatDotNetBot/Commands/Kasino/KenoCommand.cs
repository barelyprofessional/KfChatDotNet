using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class KenoCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^keno (?<difficulty>classic|low|medium|high) (?<amount>\d+) (?<numbers>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^keno (?<difficulty>classic|low|medium|high) (?<amount>\d+\.\d+) (?<numbers>\d+)$", RegexOptions.IgnoreCase),
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

    private List<int> playerNumbers;
    private List<int> casinoNumbers;
    private decimal HOUSE_EDGE = (decimal)0.98;
    private const string PlayerNumberDisplay = "⬜";
    private const string CasinoNumberDisplay = "🔶";
    private const string MatchRevealDisplay = "💠";
    private const string BlankSpaceDisplay = "⬛";
    
    private SentMessageTrackerModel? _kenoTable;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay, BuiltIn.Keys.KasinoKenoCleanupDelay,
            BuiltIn.Keys.KasinoKenoFrameDelay, BuiltIn.Keys.KasinoKenoEnabled
        ]);

        // Check if keno is enabled
        var kenoEnabled = (settings[BuiltIn.Keys.KasinoKenoEnabled]).ToBoolean();
        if (!kenoEnabled)
        {
            var gameDisabledCleanupDelay =
                TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, keno is currently disabled.",
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }

        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoKenoCleanupDelay].ToType<int>());

        if (!arguments.TryGetValue("amount", out var amount)) //if user just enters !keno
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough arguments. !keno <wager> <number between 1 and 10>, or !keno <wager> and 10 will be selected automatically",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        string difficultyString;
        if (!arguments.TryGetValue("difficulty", out var difficultyArg))
        {
            difficultyString = "high";
        }
        else
        {
            difficultyString = difficultyArg.Value;
        }
        var wager = Convert.ToDecimal(amount.Value);
        var numbers = !arguments.TryGetValue("numbers", out var userNumbers)
            ? 10
            : Convert.ToInt32(userNumbers.Value); //if user just enters !keno <wager>
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

        if (numbers is < 1 or > 10) //if user picks invalid numbers
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can only pick numbers from 1 - 10",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var payoutMultipliersHigh =
            new[,] //stole the payout multis from stake keno and re added the RTP, except for the 1000x
            {
                { 0.0, 4.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 1 selection
                { 0.0, 0.0, 17.27, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 2 selections
                { 0.0, 0.0, 0.0, 82.32, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 3 selections
                { 0.0, 0.0, 0.0, 10.1, 261.61, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 4 selections
                { 0.0, 0.0, 0.0, 4.5, 48.48, 454.54, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 5 selections
                { 0.0, 0.0, 0.0, 0.0, 11.11, 353.53, 717.17, 0.0, 0.0, 0.0, 0.0 }, // 6 selections
                { 0.0, 0.0, 0.0, 0.0, 7.07, 90.90, 404.04, 808.08, 0.0, 0.0, 0.0 }, // 7 selections
                { 0.0, 0.0, 0.0, 0.0, 5.05, 20.20, 272.72, 606.06, 909.09, 0.0, 0.0 }, // 8 selections
                { 0.0, 0.0, 0.0, 0.0, 4.04, 11.11, 56.56, 505.05, 808.08, 1000.0, 0.0 }, // 9 selections
                { 0.0, 0.0, 0.0, 0.0, 3.53, 8.08, 13.13, 63.63, 505.05, 808.08, 1000.0 } // 10 selections
            };
        var payoutMultipliersClassic =
            new[,] //stole the payout multis from stake keno and re added the RTP, except for the 1000x
            {
                { 0.0, 4.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 1 selection
                { 0.0, 1.93, 4.59, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 2 selections
                { 0.0, 1.02, 3.16, 10.6, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 3 selections
                { 0.0, 0.81, 1.83, 10.1, 5.1, 22.96, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 4 selections
                { 0.0, 0.26, 1.42, 4.18, 16.83, 36.73, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 5 selections
                { 0.0, 0.0, 1.02, 3.75, 7.14, 16.83, 40.81, 0.0, 0.0, 0.0, 0.0 }, // 6 selections
                { 0.0, 0.0, 0.46, 3.06, 4.59, 14.28, 31.63, 61.22, 0.0, 0.0, 0.0 }, // 7 selections
                { 0.0, 0.0, 0.0, 2.24, 4.08, 13.26, 22.44, 56.12, 71.42, 0.0, 0.0 }, // 8 selections
                { 0.0, 0.0, 0.0, 1.58, 3.06, 8.16, 15.30, 44.89, 61.22, 86.73, 0.0 }, // 9 selections
                { 0.0, 0.0, 0.0, 1.42, 2.29, 4.59, 8.16, 17.34, 51.02, 81.63, 102.04 } // 10 selections
            };
        var payoutMultipliersLow =
            new[,] //stole the payout multis from stake keno and re added the RTP, except for the 1000x
            {
                { 0.7, 1.85, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 1 selection
                { 0.0, 2.04, 3.87, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 2 selections
                { 0.0, 1.12, 1.4, 26.53, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 3 selections
                { 0.0, 0.0, 2.24, 8.06, 91.83, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 4 selections
                { 0.0, 0.0, 1.53, 4.28, 13.26, 306.12, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 5 selections
                { 0.0, 0.0, 1.12, 2.04, 6.32, 102.04, 714.28, 0.0, 0.0, 0.0, 0.0 }, // 6 selections
                { 0.0, 0.0, 1.12, 1.63, 3.57, 15.3, 229.59, 714.28, 0.0, 0.0, 0.0 }, // 7 selections
                { 0.0, 0.0, 1.12, 1.53, 2.04, 5.61, 39.79, 102.04, 816.32, 0.0, 0.0 }, // 8 selections
                { 0.0, 0.0, 1.12, 1.32, 1.73, 2.55, 7.65, 51.02, 255.1, 1000.0, 0.0 }, // 9 selections
                { 0.0, 0.0, 1.12, 1.22, 1.32, 1.83, 3.57, 13.26, 51.02, 255.1, 1000.0 } // 10 selections
            };
        var payoutMultipliersMedium =
            new[,] //stole the payout multis from stake keno and re added the RTP, except for the 1000x
            {
                { 0.4, 2.8, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 1 selection
                { 0.0, 1.83, 5.2, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 2 selections
                { 0.0, 0.0, 2.85, 51.02, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 3 selections
                { 0.0, 0.0, 1.73, 10.2, 102.04, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 4 selections
                { 0.0, 0.0, 1.42, 4.08, 14.28, 397.95, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 5 selections
                { 0.0, 0.0, 0.0, 3.06, 9.18, 183.67, 724.48, 0.0, 0.0, 0.0, 0.0 }, // 6 selections
                { 0.0, 0.0, 0.0, 2.04, 7.14, 30.61, 408.16, 816.32, 0.0, 0.0, 0.0 }, // 7 selections
                { 0.0, 0.0, 0.0, 2.04, 4.08, 11.22, 68.36, 408.16, 918.36, 0.0, 0.0 }, // 8 selections
                { 0.0, 0.0, 0.0, 2.04, 2.55, 11.11, 56.56, 505.05, 808.08, 1000.0, 0.0 }, // 9 selections
                { 0.0, 0.0, 0.0, 0.0, 3.53, 5.1, 15.3, 63.63, 102.04, 510.2, 1000.0 } // 10 selections
            };
        Dictionary<string, double[,]> payoutMultipliers = new Dictionary<string, double[,]>{
            { "high", payoutMultipliersHigh },
            { "low", payoutMultipliersLow},
            { "medium", payoutMultipliersMedium},
            { "classic", payoutMultipliersClassic}
        };

    playerNumbers = GenerateKenoNumbers(numbers, gambler);
        casinoNumbers = GenerateKenoNumbers(10, gambler, true);
        var matches = playerNumbers.Intersect(casinoNumbers).ToList();
        var payoutMulti = payoutMultipliers[difficultyString][numbers - 1, matches.Count];
        
        await AnimatedDisplayTable(playerNumbers, casinoNumbers, matches, botInstance);
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        decimal newBalance;
        if (payoutMulti == 0) //you lose
        {
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Keno, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]lost {await wager.FormatKasinoCurrencyAsync()}[/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}.",
                true, autoDeleteAfter: cleanupDelay);
            botInstance.ScheduleMessageAutoDelete(_kenoTable ?? throw new Exception("Cannot clean up _kenoTable as it's null"), cleanupDelay);
            return;
        }

        //you win
        var win = wager * (decimal)payoutMulti;
        // Required to avoid compiler errors when trying to format it in the win message
        newBalance = await Money.NewWagerAsync(gambler.Id, wager, wager * (decimal)payoutMulti, WagerGame.Keno, ct: ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]won {await win.FormatKasinoCurrencyAsync()} with a {payoutMulti}x multi![/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}.",
            true, autoDeleteAfter: cleanupDelay);
        botInstance.ScheduleMessageAutoDelete(_kenoTable ?? throw new Exception("Cannot clean up _kenotable as it's null"), cleanupDelay);
    }
    
    private async Task AnimatedDisplayTable(List<int> playerNumbers, List<int> casinoNumbers, List<int> matches, ChatBot botInstance)
    {
        var cleanupDelay = TimeSpan.FromMilliseconds((await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoKenoCleanupDelay)).ToType<int>());
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

        _kenoTable = await botInstance.SendChatMessageAsync(displayMessage, true);
        var i = 0;
        while (_kenoTable.ChatMessageId == null)
        {
            i++;
            if (_kenoTable.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost) return;
            if (i > 60) return;
            await Task.Delay(100);
        }

        if (_kenoTable.ChatMessageId == null)
        {
            throw new Exception($"_kenoTable chat message ID never got populated. Tracker status is: {_kenoTable?.Status}");
        }

        var frameDelay = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoKenoFrameDelay)).ToType<int>(); // this should be grabbed from the settings dict declared at the start but idk how to do it cleanly atm.
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
            await botInstance.KfClient.EditMessageAsync(_kenoTable.ChatMessageId!.Value, displayMessage);
            await Task.Delay(frameDelay);
            if (displayMessage.Length <= 79 && displayMessage.Contains(BlankSpaceDisplay) &&
                (displayMessage.Contains(CasinoNumberDisplay) || displayMessage.Contains(MatchRevealDisplay) ||
                 frame == 9)) continue; //every board should have blank spaces and casino numbers or matches. player numbers might be hidden by matches
            logger.Error($"Casino numbers: {string.Join(",", casinoNumbers)} | Player Numbers: {string.Join(",", playerNumbers)} | Matches: {string.Join(",", matches)} | Frame: {frame - 1} | Display Board:");
            logger.Error(displayMessage);
            await botInstance.SendChatMessageAsync($"Keno is bugged dewd, died on frame {frame} :bossman:", true, autoDeleteAfter: cleanupDelay);
        }
    }

    private List<int> GenerateKenoNumbers(int size, GamblerDbModel gambler, bool kasino = false)
    {
        var numbers = new List<int>();
        for (var i = 0; i < size; i++)
        {
            var repeatNum = true;
            while (repeatNum)
            {
                var randomNum = Money.GetRandomNumber(gambler, 1, 40);
                if (numbers.Contains(randomNum)) continue;
                if (kasino && Money.GetRandomDouble(gambler) > (double)HOUSE_EDGE &&
                    playerNumbers.Contains(randomNum)) continue; //rigging function
                numbers.Add(randomNum);
                repeatNum = false;
            }
        }

        return numbers;
    }
}
