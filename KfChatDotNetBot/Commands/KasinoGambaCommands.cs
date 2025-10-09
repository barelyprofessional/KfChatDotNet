using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;
using RandN;
using RandN.Compat;

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
            logger.Error($"Casino numbers: {string.Join(",", casinoNumbers)} | Player Numbers: {string.Join(",", playerNumbers)} | Matches: {string.Join(",", matches)} | Frame: {frame - 1} | Display Board:");
            logger.Error(displayMessage);
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

[KasinoCommand]
[WagerCommand]
public class Planes : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^planes (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^planes (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^planes$")
    ];
    public string? HelpText => "!planes <bet amount>";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(30)
    };

    private const string Boost = "💨";
    private const string PlaneUp = "🛫";
    private const string PlaneDown = "🛬";
    private const string PlaneExplosion = "🔥";
    private const string Bomb = "❌";
    private const string Multi = "*️⃣";
    private const string Carrier = "⛴";
    private const string Water = "🌊";
    private const string Air = "\u2B1C"; // White square
    private const string BlankSpace = "⠀"; //need 35?

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !planes <wager>", true);
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
                true);
            return;
        }

        var carrierCount = 6;
        var planesBoard = CreatePlanesBoard(gambler);
        var planesBoard2 = CreatePlanesBoard(gambler);
        var plane = new Plane(gambler);
        var frameLength = 1056.0;
        var counter = 0;
        var noseUp = true;
        var planesDisplay = GetGameBoard(-3, planesBoard, plane, carrierCount, noseUp);
        var msgId = await botInstance.SendChatMessageAsync(planesDisplay, true);
        var num = 0;
        while (msgId.ChatMessageId == null)
        {
            num++;
            if (msgId.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost) return;
            if (num > 60) return;
            await Task.Delay(100, ctx);
        }
        //place where planes used to stop working
        /*
         * new goal of basic planes game
         * static board, plane moves through the board, 25 spaces long
         * if it gets to the end, reset the board, remember plane height, and continue playing no smooth transition
         */
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(frameLength / 3), ctx);
            var neutral = false;
            var frameCounter = 0;
            if (counter % 13 < 3)
            {
                while (counter % 13 < 3)
                {
                    planesDisplay = GetGameBoard(counter%13 - 3, planesBoard, plane, carrierCount, noseUp);
                    await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
                    await Task.Delay(TimeSpan.FromMilliseconds(frameLength), ctx);
                    logger.Info($"Generated preGameFrame {counter} and waited {frameLength}ms.");
                    counter++;
                }

                logger.Info("Pregame frames successfully generated.");
            }
            else
            {
                while (!neutral)
                {
                    frameCounter++;
                    logger.Info($"FrameCounter: {frameCounter} Counter: {counter}");
                    try
                    {
                        switch (planesBoard[plane.Height, counter % 13 - 3])
                        {
                            case 0: //do nothing plane hit neutral space
                                neutral = true;
                                break;
                            case 1: //hit rocket
                                planesBoard[plane.Height, counter % 13 - 3] = 0; //plane consumes rocket
                                plane.HitRocket();
                                noseUp = false;
                                break;
                            case 2: //hit multi
                                planesBoard[plane.Height, counter % 13 - 3] = 0; //plane consumes multi
                                plane.HitMulti();
                                noseUp = true;
                                break;
                            default:
                                await botInstance.SendChatMessageAsync("Something went wrong, error code 1.", true);
                                return;
                        }
                    }
                    catch (IndexOutOfRangeException e)
                    {
                        await botInstance.SendChatMessageAsync($"Something went wrong, error code 2. Counter: {counter} Counter%: {counter %13 - 3} Height: {plane.Height}[br]{e}", true);
                        logger.Error(
                            $"Something went wrong, error code 2. Counter: {counter} Counter%: {counter % 13 - 3} Height: {plane.Height}");
                        logger.Error(e);
                        return;
                    }

                    if (neutral) //this will be the last frame so use all the remaining frame time left
                    {
                        if (frameCounter == 1) await Task.Delay(TimeSpan.FromMilliseconds(frameLength * 2 / 3), ctx); //first frame used 1/3 of frame time so 2/3 is remaining
                        else await Task.Delay(TimeSpan.FromMilliseconds(frameLength / (3 * (frameCounter - 1))), ctx);
                    }
                    else await Task.Delay(TimeSpan.FromMilliseconds(frameLength / (3 * frameCounter)), ctx); //if not the last frame use a fraction of the remaining frame time
                    planesDisplay = GetGameBoard(counter, planesBoard, plane, carrierCount, noseUp);
                    planesDisplay += $"[br]Multi: {plane.MultiTracker}x";
                    for (var i = 0; i < 10; i++)
                    {
                        planesDisplay += BlankSpace;
                    }

                    var winnings = plane.MultiTracker * wager;
                    planesDisplay += $"Winnings: {await winnings.FormatKasinoCurrencyAsync()}";
                    await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
                    if (plane.Height >= 6)
                    {
                        break;
                    }
                    //maybe fuckery around here       
                }
                counter++;
            }
            plane.Gravity();
            if (counter % 13 != 0) continue;
            planesBoard = planesBoard2;
            planesBoard2 = CreatePlanesBoard(gambler);
        } while (plane.Height < 6);
        //now plane is too low so you have either won or lost depending on your position
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var newBalance = gambler.Balance - wager;
        if (((counter % 13) - 3) % carrierCount == 0) //if you landed on the carrier
        {
            var win = plane.MultiTracker * wager - wager;
            newBalance = gambler.Balance + win;
            await Money.NewWagerAsync(gambler.Id, wager, win, WagerGame.Planes, ct: ctx);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]successfully landed with {await win.FormatKasinoCurrencyAsync()} from a total {plane.MultiTracker:N2}x multi![/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}", true);
            return;
        }
        plane.Crash();
        await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Planes, ct: ctx);
        planesDisplay = GetGameBoard(counter, planesBoard, plane, carrierCount, noseUp);
        await Task.Delay(TimeSpan.FromMilliseconds(frameLength), ctx);
        await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]crashed![/color] Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}", true);
    }

    
    private string GetGameBoard(int counter, int[,] planesBoard, Plane plane, int carrierCount, bool noseUp)
    {
        var output = "";
        for (var row = 0; row < 8; row++)
        {
            for (var column = -3; column < 10; column++) //plane starts out 3 space behind to give some space to the view,
            {
                if (row == plane.Height && column == counter - 1 && plane.JustHitMulti > 0)
                {
                    output += Boost;
                }
                else if (row == plane.Height && column == counter)
                {
                    if (plane.Crashed) output += PlaneExplosion;
                    else
                        switch (noseUp)
                        {
                            case true:
                                output += PlaneUp;
                                break;
                            case false:
                                output += PlaneDown;
                                break;
                        }
                }
                else if (column < 0) //beginning columns have no multis or bombs or carriers just air and water
                {
                    if (row != 7) output += Air;
                    else output += Water;
                }
                else if (row == 6)//row between the gameboard and where the carrier is displayed, should show the plane in this row on top of the boat on a win
                {
                    output += Air;
                }
                else if (row == 7) //water/carrier row
                {
                    if (column % carrierCount == 0) output += Carrier;
                    else output += Water;
                }
                else //this leaves rows 0-5 and columns 0-24, exactly what we need for the board
                {
                    switch (planesBoard[row, column])
                    {
                        case 0:
                            output += Air;
                            break;
                        case 1:
                            output += Bomb;
                            break;
                        case 2:
                            output += Multi;
                            break;
                    }
                }
            }

            output += "[br]";
        }
        return output;
    }

    private int[,] CreatePlanesBoard(GamblerDbModel gambler)
    {
        var board = new int [6, 10];
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 10; column++)
            {
                var randomNum = Money.GetRandomNumber(gambler, 1, 100);
                if (randomNum < 35)
                {
                    board[row, column] = 0; //neutral
                }
                else if (randomNum > 69)
                {
                    board[row, column] = 1; //rocket
                }
                else
                {
                    board[row, column] = 2; //multi
                }
            }
        }
        
        return board;
    }
}
public class Plane(GamblerDbModel gambler)
{
    public int Height = 1;
    public decimal MultiTracker = 1;
    public int JustHitMulti = 1;
    private readonly RandomShim<StandardRng> _random = RandomShim.Create(StandardRng.Create());
    public bool Crashed = false;

    public void HitRocket()
    {
        Gravity();
        MultiTracker /= 2;
    }

    public void Gravity()
    {
        if (JustHitMulti > 0)
        {
            JustHitMulti--;
            return;
        }
        Height++;
    }

    public void Crash()
    {
        MultiTracker = 0;
        Crashed = true;
    }

    public void HitMulti()
    {
        var randomNum = Money.GetRandomNumber(gambler, 0, 1);
        var weightedRand = WeightedRandomNumber(1, 10);
        if (randomNum == 0)
        {
            MultiTracker += weightedRand;
        }
        else
        {
            MultiTracker *= weightedRand;
        }

        if (Height > 0) Height--;
        JustHitMulti++;
    }

    private int WeightedRandomNumber(int min, int max)
    {
        var range = max - min + 1;
        var weight = 4.5 + Height;
        var r = _random.NextDouble();
        var exp = -Math.Log(1 - r) / weight;
        var returnVal = min + (int)Math.Round(exp * range);
        return Math.Clamp(returnVal, min, max);
    }
}