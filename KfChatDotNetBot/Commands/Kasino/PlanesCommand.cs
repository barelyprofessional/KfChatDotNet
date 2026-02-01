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
    private bool _rigged = false;
    private bool _riggedWin = false;
    private const int CarrierCount = 6;
    private decimal HOUSE_EDGE = (decimal)0.98;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay, BuiltIn.Keys.KasinoPlanesEnabled,
            BuiltIn.Keys.KasinoPlanesCleanupDelay, BuiltIn.Keys.KasinoPlanesRandomRiggeryEnabled,
            BuiltIn.Keys.KasinoPlanesTargetedRiggeryEnabled, BuiltIn.Keys.KasinoPlanesTargetedRiggeryVictims
        ]);
        
        // Check if planes is enabled
        var planesEnabled = (settings[BuiltIn.Keys.KasinoPlanesEnabled]).ToBoolean();
        if (!planesEnabled)
        {
            var gameDisabledCleanupDelay= TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, planes is currently disabled.", 
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
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

        if (HOUSE_EDGE < 1)
        {
            if (Money.GetRandomDouble(gambler, 1) > (double)HOUSE_EDGE)
            {
                _rigged = true;
            }
        }
        else
        {
            if ((double)HOUSE_EDGE - Money.GetRandomDouble(gambler, 1) > 1)
            {
                _riggedWin = true;
            }
        }
        
        var planesBoard = CreatePlanesBoard(gambler,0);
        var planesBoard2 = CreatePlanesBoard(gambler);
        var planesBoard3 = CreatePlanesBoard(gambler);
        List<int[,]> planesBoards = [planesBoard, planesBoard2, planesBoard3];
        var plane = new Plane(gambler);
        const double frameLength = 1000.0;
        var fullCounter = 0;
        var noseUp = true;
        var planesDisplay = GetPreGameBoard(-3, planesBoard2, plane, CarrierCount, noseUp);
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
            var counter = (fullCounter - 3) % 24;
            
            await Task.Delay(TimeSpan.FromMilliseconds(frameLength / 3), ctx);

            if (fullCounter >= 3)
            {
                planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, CarrierCount, noseUp);
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
                    counter = (fullCounter - 3) % 24;
                    planesDisplay = GetPreGameBoard(fullCounter, planesBoard2, plane, CarrierCount, noseUp);
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
                        planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, CarrierCount, noseUp);
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
                    if (plane.Height > 5)
                    {
                        break;
                    }
                    //maybe fuckery around here
                }
                fullCounter++;
                if ((fullCounter - 3) % 24 == 0 && fullCounter != 3)
                {
                    planesBoards.RemoveAt(0);
                    planesBoards.Add(CreatePlanesBoard(gambler));
                }
            }
            plane.Gravity();
            //maybe need to add one more frame here?***************
        } while (plane.Height < 6);
        //now plane is too low so you have either won or lost depending on your position
        var colors =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
        decimal newBalance;
        if ((fullCounter - 3) % CarrierCount == 0) //if you landed on the carrier
        {
            var win = plane.MultiTracker * wager;
            newBalance = await Money.NewWagerAsync(gambler.Id, wager, win, WagerGame.Planes, ct: ctx);
            planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, CarrierCount, noseUp);
            await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value, planesDisplay);
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you [color={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]successfully landed with {await win.FormatKasinoCurrencyAsync()} from a total {plane.MultiTracker:N2}x multi![/color]. Your balance is now: {await newBalance.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
            botInstance.ScheduleMessageAutoDelete(msgId, cleanupDelay);
            return;
        }
        plane.Crash();
        newBalance = await Money.NewWagerAsync(gambler.Id, wager, -wager, WagerGame.Planes, ct: ctx);
        planesDisplay = GetGameBoard(fullCounter, planesBoards, plane, CarrierCount, noseUp);
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
        var counter = (fullCounter - 3) % 24;
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
        // worldXPlane is the absolute distance the plane has traveled from the start.
        int worldXPlane = fullCounter - 3; 

        for (var row = 0; row < 8; row++)
        {
            for (var column = -3; column < 10; column++)
            {
                // worldXTile is the absolute coordinate of the specific tile we are currently drawing.
                int worldXTile = worldXPlane + column;

                // 1. WATER & CARRIER ROW (Row 7)
                if (row == 7)
                {
                    // We use worldXTile so the carrier stays pinned to a global position.
                    if (worldXTile >= 0 && worldXTile % carrierCount == 0) output += Carrier;
                    else output += Water;
                    continue;
                }

                // 2. THE PLANE (At Column 0 relative to the camera)
                if (row == plane.Height && column == 0)
                {
                    if (plane.Crashed) output += PlaneExplosion;
                    else output += noseUp ? PlaneUp : PlaneDown;
                    continue;
                }

                // 3. BOOST EFFECT
                if (row == plane.Height && column == -1 && plane.JustHitMulti > 1)
                {
                    output += Boost;
                    continue;
                }

                // 4. THE SKY & GAME OBJECTS (Rows 0-6)
                // Row 6 is always Air. Any tile with a negative world coordinate is also Air.
                if (row == 6 || worldXTile < 0)
                {
                    output += Air;
                }
                else
                {
                    // Calculate which BOARD the tile belongs to (0, 1, 2, 3...)
                    int boardNumber = worldXTile / 24; 
                    int localX = worldXTile % 24;

                    // Map the boardNumber to our sliding window (List of 3 boards).
                    // Our list always contains: [Board N-1, Board N, Board N+1]
                    // relative to where the plane is currently flying.
                    int planeBoardNumber = worldXPlane / 24;
                    int listIndex = boardNumber - (planeBoardNumber - 1);

                    if (listIndex >= 0 && listIndex < planesBoards.Count)
                    {
                        int tileValue = planesBoards[listIndex][row, localX];
                        output += tileValue switch
                        {
                            1 => Bomb,
                            2 => Multi,
                            _ => Air
                        };
                    }
                    else
                    {
                        // Fallback if the tile is beyond our current 3-board window
                        output += Air;
                    }
                }
            }
            output += "[br]";
        }
        return output;
    }

    private int[,] CreatePlanesBoard(GamblerDbModel gambler, int forceTiles = -1)
    {
        var board = new int [6, 24];
        
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 24; column++)
            {
                var randomNum = Money.GetRandomNumber(gambler, 0, 100);
                if (forceTiles != -1) board[row, column] = forceTiles;
                else if (_rigged && (column == 5 || column == 11 || column == 17 || column == 23) && row == 5)
                {
                    board[row, column] = 2;
                }
                else if (_riggedWin && (column == 5 || column == 11 || column == 17 || column == 23) && row == 5)
                {
                    board[row, column] = 0;
                }
                else if (_riggedWin && row == 5 && (column != 5 && column != 11 && column != 17 && column != 23))
                {
                    board[row, column] = 2;
                }
                else
                    board[row, column] = randomNum switch
                    {
                        < 49 => 0,
                        > 79 => 1,
                        _ => 2
                    };
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
        var weight = 6.55 + Height;
        var r = _random.NextDouble();
        var exp = -Math.Log(1 - r) / weight;
        var returnVal = min + (int)Math.Round(exp * range);
        return Math.Clamp(returnVal, min, max);
    }
}
