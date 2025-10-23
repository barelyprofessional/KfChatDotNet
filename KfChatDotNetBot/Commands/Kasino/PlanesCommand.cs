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

namespace KfChatDotNetBot.Commands.Kasino;

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
    public TimeSpan Timeout => TimeSpan.FromSeconds(120);
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
    private bool rigged = false;
    private bool superRigged = false;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoPlanesCleanupDelay, BuiltIn.Keys.KasinoPlanesRandomRiggeryEnabled,
            BuiltIn.Keys.KasinoPlanesTargetedRiggeryEnabled, BuiltIn.Keys.KasinoPlanesTargetedRiggeryVictims
        ]);
        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoPlanesCleanupDelay].ToType<int>());
        var logger = LogManager.GetCurrentClassLogger();
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !planes <wager>",
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

        var carrierCount = 6;
        var planesBoard = CreatePlanesBoard(gambler,0);
        var planesBoard2 = CreatePlanesBoard(gambler);
        var planesBoard3 = CreatePlanesBoard(gambler);
        if (rigged)
        {
            planesBoard2 = RigPlanesBoard(planesBoard2, carrierCount, 0);
            planesBoard3 = RigPlanesBoard(planesBoard3, carrierCount, 0);
        }
        List<int[,]> planesBoards = new List<int[,]>(){planesBoard, planesBoard2, planesBoard3};
        var plane = new Plane(gambler);
        var frameLength = 1000.0;
        var fullCounter = 0;
        bool firstBoard = true;
        var counter = 0;
        var noseUp = true;
        var planesDisplay = GetPreGameBoard(-3, planesBoard2, plane, carrierCount, noseUp);
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
            if ((fullCounter-3) > 19) firstBoard = false;
            counter = (fullCounter - 3) % 20;
            
            await Task.Delay(TimeSpan.FromMilliseconds(frameLength / 3), ctx);

            if (fullCounter >= 3)
            {
                planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, carrierCount, noseUp);
                planesDisplay += $"[br]Multi: {plane.MultiTracker}x";
                for (var i = 0; i < 10; i++)
                {
                    planesDisplay += BlankSpace;
                }
                var winnings = plane.MultiTracker * wager;
                planesDisplay += $"Winnings: {await winnings.FormatKasinoCurrencyAsync()}";
                await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
            }
            
            var neutral = false;
            var frameCounter = 0;
            if (fullCounter < 3)
            {
                while (fullCounter < 3)
                {
                    counter = fullCounter % 23 - 3;
                    planesDisplay = GetPreGameBoard(fullCounter, planesBoard2, plane, carrierCount, noseUp);
                    await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
                    await Task.Delay(TimeSpan.FromMilliseconds(frameLength), ctx);
                    fullCounter++;
                }
            }
            else
            {
                while (!neutral)
                {
                    frameCounter++;
                    try
                    {
                        /*
                         *
                         * USE BOARD 0: only used to pull the values from the previous board, never used for game determinations
                         * USE BOARD 1: always
                         * USE BOARD 2: never used for game determinations only displays
                         */
                        //if (fullCounter == 3) logger.Info($"Generating first plane impact outcome. Framecounter: {frameCounter} | FullCounter: {fullCounter} | Counter: {counter}");
                        
                        //else logger.Info($"Failed to select proper gameboard for gameplay outcome. UseBoard: {1} | FullCounter: {fullCounter} | Counter: {counter} | Height: {plane.Height} | FrameCounter: {frameCounter}");
                        switch (planesBoards[1][plane.Height, counter])
                        {
                          
                            case 0: //do nothing plane hit neutral space
                                neutral = true;
                                //if (fullCounter == 3) logger.Info($"Generated first plane impact outcome. Framecounter: {frameCounter} | FullCounter: {fullCounter} | Counter: {counter} | Outcome: neutral");
                                break;
                            case 1: //hit rocket
                                planesBoards[1][plane.Height, counter] = 0; //plane consumes rocket
                                plane.HitRocket();
                                noseUp = false;
                                //if (fullCounter == 3) logger.Info($"Generated first plane impact outcome. Framecounter: {frameCounter} | FullCounter: {fullCounter} | Counter: {counter} | Outcome: bomb");
                                break;
                            case 2: //hit multi
                                planesBoards[1][plane.Height, counter] = 0; //plane consumes multi
                                plane.HitMulti();
                                noseUp = true;
                                //if (fullCounter == 3) logger.Info($"Generated first plane impact outcome. Framecounter: {frameCounter} | FullCounter: {fullCounter} | Counter: {counter} | Outcome: multi");
                                break;
                            default:
                                await botInstance.SendChatMessageAsync("Something went wrong, error code 1.", true, autoDeleteAfter: cleanupDelay);
                                return;
                        }
                    }
                    catch (IndexOutOfRangeException e)
                    {
                        logger.Error(
                            $"Something went wrong, error code 2. Counter: {fullCounter} Counter%: {counter} Height: {plane.Height}");
                        logger.Error(e);
                        return;
                    }

                    if (neutral) //this will be the last frame so use all the remaining frame time left
                    {
                        if (frameCounter == 1) await Task.Delay(TimeSpan.FromMilliseconds(frameLength * 2 / 3), ctx); //first frame used 1/3 of frame time so 2/3 is remaining
                        else await Task.Delay(TimeSpan.FromMilliseconds(frameLength / (3 * (frameCounter - 1))), ctx);
                    }
                    else await Task.Delay(TimeSpan.FromMilliseconds(frameLength / (3 * frameCounter)), ctx); //if not the last frame use a fraction of the remaining frame time

                    try
                    {
                        planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, carrierCount, noseUp);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e);
                        throw;
                    }
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
                fullCounter++;
            }
            plane.Gravity();
            if ((fullCounter - 3) % 20 == 0 && fullCounter != 3)//removes old planesboard, adds new planeboard when necessary **********************************************************************NEEDS MORE UPDATES
            {
                if (Money.GetRandomNumber(gambler, 0, 100) == 0 && settings[BuiltIn.Keys.KasinoPlanesRandomRiggeryEnabled].ToBoolean()) rigged = true;
                if (settings[BuiltIn.Keys.KasinoPlanesTargetedRiggeryEnabled].ToBoolean() &&
                    settings[BuiltIn.Keys.KasinoPlanesTargetedRiggeryVictims].JsonDeserialize<List<int>>()!.Contains(user.KfId))
                {
                    rigged = true;
                }
                logger.Info($"Switching planes boards. FullCounter: {fullCounter} | Counter: {counter}");
                planesBoards.RemoveAt(0);
                planesBoards.Add(CreatePlanesBoard(gambler));
                if (rigged && Money.GetRandomNumber(gambler, 0, 100) == 0) {
                    planesBoards[1] = CreatePlanesBoard(gambler, 1); //1% chance to update to a board full of rockets if rigged
                    superRigged = true;
                }
                else if (rigged)
                {
                    planesBoards[1] = RigPlanesBoard(planesBoards[1], carrierCount, fullCounter);
                    planesBoards[2] = RigPlanesBoard(planesBoards[2], carrierCount, fullCounter);
                }
            }
        } while (plane.Height < 6);
        //now plane is too low so you have either won or lost depending on your position
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        var newBalance = gambler.Balance - wager;
        if ((fullCounter - 3) % carrierCount == 0) //if you landed on the carrier
        {
            var win = plane.MultiTracker * wager;
            newBalance = gambler.Balance + win;
            await Money.NewWagerAsync(gambler.Id, wager, win, WagerGame.Planes, ct: ctx);
            planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, carrierCount, noseUp);
            await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]successfully landed with {await win.FormatKasinoCurrencyAsync()} from a total {plane.MultiTracker:N2}x multi![/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
            botInstance.ScheduleMessageAutoDelete(msgId, cleanupDelay);
            return;
        }
        plane.Crash();
        await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Planes, ct: ctx);
        planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, carrierCount, noseUp);
        await Task.Delay(TimeSpan.FromMilliseconds(frameLength), ctx);
        await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]crashed![/color] Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
        botInstance.ScheduleMessageAutoDelete(msgId, cleanupDelay);
    }

    private string GetPreGameBoard(int fullCounter, int[,] planesBoard, Plane plane, int carrierCount, bool noseUp)
    {
        //counter < 5
        var counter = fullCounter % 23 - 3;
        var output = "";
        for (var row = 0; row < 8; row++)
        {
            for (var column = -3; column < 10; column++) //plane starts out 3 space behind to give some space to the view,
            {
                if (row == plane.Height && column == counter - 1 && plane.JustHitMulti > 1)
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

    private string GetGameBoard(int fullCounter, List<int[,]> planesBoards, Plane plane, int carrierCount, bool noseUp)
    {
        var output = "";
        int counter;
        var logger = LogManager.GetCurrentClassLogger();
        
        for (var row = 0; row < 8; row++)
        {
            for (var column = -3;
                 column < 10;
                 column++) //plane starts out 3 space behind to give some space to the view,
            {
                var useBoard = 1;
                if (fullCounter < 23) counter = fullCounter % 23 - 3;
                else counter = (fullCounter - 3) % 20;
                //---
                if (counter + column < 0)
                {
                    counter = 20 + counter;
                    useBoard = 0;
                }
                else if (counter + column > 19)
                {
                    useBoard = 2;
                }

                //---actual game board displays below here
                if (row == plane.Height && column == -1 && plane.JustHitMulti > 1)
                {
                    output += Boost;
                }
                else if (row == 7) //water/carrier row
                {
                    if (((fullCounter - 3)+ column) % carrierCount == 0) output += Carrier;
                    else output += Water;
                }
                else if (row == plane.Height && column == 0)
                {
                    if (plane.Crashed) output += PlaneExplosion;
                    else
                    {
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
                }
                else if (row == 6) output += Air;
                else
                {
                    //logger.Info($"GetGameBoard: attempting to access planeboard index [{row},{(column + counter) % 20}]. RawCounter: {fullCounter} | Counter: {counter} | UseBoard: {useBoard}");
                    switch (planesBoards[useBoard][row, (counter + column) % 20])
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
            // Was https://i.postimg.cc/rmX59qtV/avelloonaircall2.webp previously
            if (superRigged && row == 0) output += "[img]https://i.ddos.lgbt/u/6v8WJ5.webp[/img]";
            output += "[br]";
        }
        return output;
    }

    private int[,] CreatePlanesBoard(GamblerDbModel gambler, int forceTiles = -1)
    {
        var board = new int [6, 20];
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 20; column++)
            {
                var randomNum = Money.GetRandomNumber(gambler, 1, 100);
                if (forceTiles != -1) board[row, column] = forceTiles;
                else if (randomNum < 49)
                {
                    board[row, column] = 0; //neutral
                }
                else if (randomNum > 79)
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

    private int[,] RigPlanesBoard(int[,] planesBoard, int carrierCount, int fullCounter)
    {
        int[,] returnBoard = new int[6,20];
        int boardCounter = (fullCounter-3) / 20;
        var spaceToUpdate = 0;
        bool startUpdating = true;
        spaceToUpdate = (fullCounter-3) % 20; //how far along is the game into the current board
        if (spaceToUpdate > 0) startUpdating = false;
        
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 20; column++)
            {
                if (column >= spaceToUpdate) startUpdating = true;
                else startUpdating = false;
                if (startUpdating)
                {
                    if (row == 5 && column+1 == (fullCounter-3) % carrierCount)
                    {
                        returnBoard[row, column] = 2; //force set as multi
                    }
                    else
                    {
                        returnBoard[row, column] = planesBoard[row, column];
                    }
                }
                else
                {
                    returnBoard[row, column] = planesBoard[row, column];
                }
                
            }
        }
        return returnBoard;
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
        if (JustHitMulti > 0) JustHitMulti--;
        else if (Height >= 6) Height = 6;
        else Height++;
    }

    public void Crash()
    {
        MultiTracker = 0;
        Crashed = true;
    }

    public void HitMulti()
    {
        var randomNum = Money.GetRandomNumber(gambler, 0, 4);
        var weightedRand = WeightedRandomNumber(1, 10);
        if (randomNum == 0)
        {
            MultiTracker *= weightedRand + (decimal)0.1;
        }
        else
        {
            MultiTracker += weightedRand;
        }

        if (Height > 0) Height--;
        if (JustHitMulti == 0) JustHitMulti++;
        if (JustHitMulti < 6) JustHitMulti++;
    }

    private int WeightedRandomNumber(int min, int max)
    {
        var range = max - min + 1;
        var weight = 6.25 + Height;
        var r = _random.NextDouble();
        var exp = -Math.Log(1 - r) / weight;
        var returnVal = min + (int)Math.Round(exp * range);
        return Math.Clamp(returnVal, min, max);
    }
}