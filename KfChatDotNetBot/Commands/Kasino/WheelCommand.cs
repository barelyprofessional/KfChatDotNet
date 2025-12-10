using System.Text.RegularExpressions;
using System.Globalization;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class WheelCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"wheel (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"wheel (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"wheel (?<amount>\d+) (?<difficulty>[A-Za-z]+)$", RegexOptions.IgnoreCase),
        new Regex(@"wheel (?<amount>\d+\.\d+) (?<difficulty>[A-Za-z]+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText =>
        "Its wheel but oval shaped and shit";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(12);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(15)
    };
    //private static double _houseEdge = 0.015; // house edge hack?
    
    // game assets
    private const string LOW_DIFFICULTY_WHEEL = "🟢⚪⚪⚪⚫⚪⚪⚪⚪⚫🟢⚪⚪⚪⚫⚪⚪⚪⚪⚫";
    private const string MEDIUM_DIFFICULTY_WHEEL = "🟢⚫🟡⚫🟡⚫🟡⚫🟢⚫🟣⚫⚪⚫🟡⚫🟡⚫🟡⚫";
    private const string HIGH_DIFFICULTY_WHEEL = "⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫🔴";
    private const string MIDDLE_WHEEL_FILL = "....................⮝....................";
    // game settings
    private const int MIN_WHEELSPIN_DELAY = 200;
    private const int MAX_WHEELSPIN_DELAY = 1000;
    private static readonly Dictionary<string, decimal> LOW_DIFF_MULTIS = new()
    {
        { "⚫", 0.00m },
        { "⚪", 1.20m },
        { "🟢", 1.50m }
    };
    private static readonly Dictionary<string, decimal> MEDIUM_DIFF_MULTIS = new()
    {
        { "⚫", 0.00m },
        { "🟢", 1.50m },
        { "⚪", 1.80m },
        { "🟡", 2.00m },
        { "🟣", 3.00m }
    };
    private static readonly Dictionary<string, decimal> HIGH_DIFF_MULTIS = new()
    {
        { "⚫", 0.00m },
        { "🔴", 19.80m }
    };


    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromMilliseconds((await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoGuessWhatNumberCleanupDelay)).ToType<int>());
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !wheel <wager> <difficulty: low, medium, high>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        var difficulty = arguments["difficulty"].Success ? Convert.ToString(arguments["difficulty"].Value) : new[] {"low", "medium", "high"}[Money.GetRandomNumber(gambler, 0,2)];
        if (difficulty.ToLower() is not ("l" or "low" or "m" or "medium" or "h" or "high"))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, unrecognized difficulty selection, please choose between: low, medium, high", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var wager = Convert.ToDecimal(amount.Value);
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var wheel = difficulty.ToLower() switch
        {
            ("l" or "low") => new Wheel(gambler, LOW_DIFFICULTY_WHEEL, MIDDLE_WHEEL_FILL, 0),
            ("m" or "medium") => new Wheel(gambler, MEDIUM_DIFFICULTY_WHEEL, MIDDLE_WHEEL_FILL, 1),
            ("h" or "high") => new Wheel(gambler, HIGH_DIFFICULTY_WHEEL, MIDDLE_WHEEL_FILL, 2),
            _ => null
        };
        if (wheel == null)
            throw new InvalidOperationException($"Something went horribly wrong, couldn't initialize wheel based on difficulty selection");
        // choose target to land on after wheelspin
        var target = wheel.GetWheelElements()[Money.GetRandomNumber(gambler,0, wheel.GetWheelElements().Count)];
        var stepsToTarget = wheel.ComputeGameStepsToTarget(target);
        var wheelDisplayMessage = await botInstance.SendChatMessageAsync(wheel.ConvertWheelToOvalString(),
            true, autoDeleteAfter: cleanupDelay);
        while (wheelDisplayMessage.Status != SentMessageTrackerStatus.ResponseReceived)
        {
            await Task.Delay(100, ctx); // wait until first message is fully sent
        }
        // main loop
        for (int i = 0; i < stepsToTarget; i++)
        {
            double t = (double)i / (stepsToTarget - 1);
            double easeOut = 1 - Math.Pow(1 - t, 3); // cubic ease-out curve for 'realistic' wheelspin animation

            int delay = (int)(MIN_WHEELSPIN_DELAY + easeOut * (MAX_WHEELSPIN_DELAY - MIN_WHEELSPIN_DELAY));
            await Task.Delay(delay, ctx);
            wheel.RotateWheelOnce();
            await botInstance.KfClient.EditMessageAsync(wheelDisplayMessage.ChatMessageId!.Value,
                wheel.ConvertWheelToOvalString());
        }
        
        // payout logics
        var multi = -1.0m;
        if (wheel.GetDifficulty() == 0) multi = LOW_DIFF_MULTIS[target];
        if (wheel.GetDifficulty() == 1) multi = MEDIUM_DIFF_MULTIS[target];
        if (wheel.GetDifficulty() == 2) multi = HIGH_DIFF_MULTIS[target];
        if (multi == -1.0m)
            throw new InvalidOperationException($"Could not derrive multi from target: {target} on wheel diff {wheel.GetDifficulty()}");
        var win = multi != 0.00m;
        
        string wheelResultMessage;
        decimal newBalance;
        if (win)
        {
            var wheelPayout = Math.Round(wager * multi - wager, 2);
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, wheelPayout, WagerGame.Wheel, ct: ctx);
            wheelResultMessage = $"{user.FormatUsername()}, you spun a {multi}x and [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]WON[/COLOR][/B]" +
                                 $" your balance is {await newBalance.FormatKasinoCurrencyAsync()}";
        }
        else
        {
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Wheel, ct: ctx);
            wheelResultMessage = $"{user.FormatUsername()}, you spun a {multi}x and [B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]LOST[/COLOR][/B]" +
                                 $", better luck next time. Your balance is  {await newBalance.FormatKasinoCurrencyAsync()}";
            
        }
        await botInstance.SendChatMessageAsync(wheelResultMessage, true, autoDeleteAfter: cleanupDelay);
    }
}

public class Wheel
{
    private readonly GamblerDbModel _gambler;
    private List<string> _wheelElements;
    private readonly string _middleFill;
    private readonly int _difficulty; // 0 = low, 1 = medium, 2 = high

    public Wheel(GamblerDbModel gambler, string wheelString, string middleFill, int difficulty)
    {
        _gambler = gambler;
        _wheelElements = ExtractTextElements(wheelString);
        if (_wheelElements.Count != 20)
            throw new ArgumentException("Wheel must be exactly 20 elements.");
        _middleFill = middleFill;
        _difficulty = difficulty;
        
        RandomizeInitialState(); // start wheel in random state
    }

    public List<string> GetWheelElements() => _wheelElements;
    public int GetDifficulty() => _difficulty;
    
    // Extract grapheme clusters, safe for emojis, stolen from AI
    private static List<string> ExtractTextElements(string rawWheel)
    {
        List<string> wheelElements = new();
        TextElementEnumerator e = StringInfo.GetTextElementEnumerator(rawWheel);
        while (e.MoveNext())
            wheelElements.Add((string)e.Current);
        return wheelElements;
    }

    private void RandomizeInitialState()
    {
        int shift = Money.GetRandomNumber(_gambler, 0, 19);
        RotateWheel(shift);
    }

    private void RotateWheel(int steps)
    {
        steps %= _wheelElements.Count;
        if (steps <= 0) return;
        int cut = _wheelElements.Count - steps;
        List<string> rotated = new();
        // Last N + first 20-N
        rotated.AddRange(_wheelElements.GetRange(cut, steps));
        rotated.AddRange(_wheelElements.GetRange(0, cut));
        _wheelElements = rotated;
    }

    public void RotateWheelOnce() => RotateWheel(1);

    public int ComputeGameStepsToTarget(string target)
    {
        // start by first doing 1-3 full rotations of the wheel
        int fullRotations = Money.GetRandomNumber(_gambler, 1, 3);
        int steps = fullRotations * 20;
        // find how many more steps until wheel index 4 (top middle) == target
        int extra = StepsUntilIndex4Match(target);
        return steps + extra;
    }

    private int StepsUntilIndex4Match(string target)
    {
        List<string> temp = new List<string>(_wheelElements);
        for (int step = 0; step < 20; step++)
        {
            if (temp[4] == target)
                return step;
            // sim rotation
            int cut = temp.Count - 1;
            string last = temp[cut];
            temp.RemoveAt(cut);
            temp.Insert(0, last);
        }
        return 0; // should always find in 20 steps;
    }

    public string ConvertWheelToOvalString()
    {
        // top row indices 0..8
        string top = String.Concat(_wheelElements.GetRange(0, 9));
        // middle row, left index 19, right index 9
        string middle = _wheelElements[19] + _middleFill + _wheelElements[9];
        // bottom row indices 10..18 but reversed so 18..10
        var reversedBottom = new List<string>(9);
        for (int i = 18; i >= 10; i--)
            reversedBottom.Add(_wheelElements[i]);
        string bottom = string.Concat(reversedBottom);
        return $"{top}\n{middle}\n{bottom}";
    }
}