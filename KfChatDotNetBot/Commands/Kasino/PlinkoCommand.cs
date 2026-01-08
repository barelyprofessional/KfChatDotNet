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

public class PlinkoCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^plinko (?<amount>\d+\.\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^plinko (?<amount>\d+) (?<number>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^plinko (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^plinko (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex("^plinko")
    ];
    public string? HelpText => "!plinko <bet amount> <optional number of balls 1 - 10, default 1 if nothing entered>";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(10)
    };

    private readonly string NULLSPACE = "⚫";
    private readonly string EMPTYSPACE = "⚪";
    private readonly string BALL = "🟠";
    private readonly int DIFFICULTY = 7;//maybe plan to allow user to change difficulty of plinko in future updates, would need to change the payout logic though
    private static readonly double VACUUM = 0.02;
    
    private static readonly Dictionary<int, decimal> PlinkoPayoutBoard = new()
    {
        {0, 8},
        {1, (decimal)0.5},
        {2, (decimal)0.25},
        {3, (decimal)0.25},
        {4, (decimal)0.25},
        {5, (decimal)0.5},
        {6, 8},
        
    };
    private static readonly List<(int row, int col)> validPositions = new() //would need to come up with a formula to make this to have user defined difficulty, good luck
    {
                               (0, 3),
                       (1, 2),         (1, 4),
                       (2, 2), (2, 3), (2, 4),
                (3, 1),(3, 2),         (3, 4), (3, 5),
                (4, 1),(4, 2), (4, 3), (4, 4), (4, 5),
        (5, 0), (5, 1),(5, 2),         (5, 4), (5, 5), (5, 6),
        (6, 0), (6, 1),(6, 2), (6, 3), (6, 4), (6, 5), (6, 6)
    };
    //      1       1       1       1       1       1       1
    //      2       1       0.5     1       0.5     1       2
    //      4       0.5     0.5     0.25    0.5     0.5     4
    //      8       0.5     0.25    0.25    0.25    0.5     8
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        decimal payout = 0;
        decimal currentPayout = 0;
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoPlinkoCleanupDelay, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor,
            BuiltIn.Keys.KasinoPlinkoEnabled, BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay
        ]);

        if (!settings[BuiltIn.Keys.KasinoPlinkoEnabled].ToBoolean())
        {
            var gameDisabledCleanupDelay= TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, plinko is currently disabled.", 
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoPlinkoCleanupDelay].ToType<int>());
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !plinko <wager> <optional number of balls default 1>",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var wager = Convert.ToDecimal(amount.Value);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        int numberOfBalls = 0;
        if (!arguments.TryGetValue("number", out var number))
        {
            numberOfBalls = 1;
        }
        else numberOfBalls = Convert.ToInt32(number.Value);

        if (numberOfBalls < 1 || numberOfBalls > 10)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only play with 1 - 10 balls at a time", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        if (gambler.Balance < wager * numberOfBalls)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        List<PlinkoBall> ballsNotInPlay = new List<PlinkoBall>();
        List<PlinkoBall> ballsInPlay = new List<PlinkoBall>();
        for (int i = 0; i < numberOfBalls; i++)
        {
            ballsNotInPlay.Add(new PlinkoBall());
        }
        //game starts here
        int breakCounter = 0;
        var plinkoMessageID = await botInstance.SendChatMessageAsync(PlinkoBoardDisplay(ballsInPlay), true, autoDeleteAfter: cleanupDelay);
        while (plinkoMessageID.ChatMessageId == null && breakCounter < 1000) { 
            await Task.Delay(100);
            breakCounter++;
        }
        if (breakCounter >= 999){
            throw new Exception("game broke while waiting for chat message id");
        }
        breakCounter = 0;
        var logger = LogManager.GetCurrentClassLogger();
        while (ballsNotInPlay.Count > 0 || ballsInPlay.Count > 0)
        {
            breakCounter++;
            if (breakCounter >= numberOfBalls * 10) throw new Exception("stuck in while loop in plinko");
            currentPayout = 0;
            if (ballsNotInPlay.Count > 0)
            {
                ballsInPlay.Add(ballsNotInPlay[0]);
                ballsNotInPlay.RemoveAt(0);
            }
            await botInstance.KfClient.EditMessageAsync(plinkoMessageID.ChatMessageId!.Value,PlinkoBoardDisplay(ballsInPlay));
            if (ballsInPlay[0].POSITION.row == DIFFICULTY - 1) //once your ball has reached the bottom calculate the payout
            {
                currentPayout = wager * PlinkoPayoutBoard[ballsInPlay[0].POSITION.col];
                payout += currentPayout;
                if (currentPayout == wager * 25) logger.Info($"Plinko: Max win on plinko, ball position: ({ballsInPlay[0].POSITION.row}, {ballsInPlay[0].POSITION.col})");
                if (currentPayout > wager)
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value!}]won[/color] ${currentPayout} KKK from a plinko ball worth {wager}!", true, autoDeleteAfter: TimeSpan.FromSeconds(5));
                }
                else
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsRedColor].Value!}lost[/color] ${wager-currentPayout} KKK from a plinko ball worth {wager}.", true, autoDeleteAfter: TimeSpan.FromSeconds(5));
                }
                ballsInPlay.RemoveAt(0);
            }
            foreach (var ball in ballsInPlay)
            {
                ball.Iterate();
            }

            await Task.Delay(250);
            await botInstance.KfClient.EditMessageAsync(plinkoMessageID.ChatMessageId!.Value,PlinkoBoardDisplay(ballsInPlay));
            await Task.Delay(250);

        }
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager*numberOfBalls, payout, WagerGame.Plinko, ct: ctx);
        await botInstance.SendChatMessageAsync($"[u]{user.FormatUsername()}, you won ${payout} KKK from {numberOfBalls} plinko balls worth ${wager} KKK. Balance: ${newBalance} KKK", true, autoDeleteAfter: cleanupDelay);
        
    }

    public string PlinkoBoardDisplay(List<PlinkoBall> balls)
    {
        string board = "";
        bool spaceIsBall = false;
        bool spaceIsValid = false;
        
        for (int row = 0; row < DIFFICULTY; row++)
        {
            for (int col = 0; col < DIFFICULTY; col++)
            {
                spaceIsBall = false;
                spaceIsValid = false;
                foreach (var position in validPositions)
                {
                    if (position.row == row && position.col == col)
                    {
                        spaceIsValid = true;
                        foreach (var ball in balls)
                        {
                            if (ball.POSITION.row == row && ball.POSITION.col == col)
                            {
                                board += BALL;
                                spaceIsBall = true;
                                break;
                            }
                        }

                        if (!spaceIsBall) board += EMPTYSPACE;
                    }
                }

                if (!spaceIsValid) board += NULLSPACE;

            }

            board += "[br]";
        }

        return board;
    }
    public class PlinkoBall
    {
        private RandomShim<StandardRng> RAND = RandomShim.Create(StandardRng.Create());
        public (int row, int col) POSITION;
        private Dictionary<int, List<int>> validColumnsForRow;
        public PlinkoBall()
        {
            POSITION = (0, 3);
            validColumnsForRow = new Dictionary<int, List<int>>();
            foreach (var position in validPositions){
                if (!validColumnsForRow.ContainsKey(position.row)) validColumnsForRow.Add(position.row, new List<int>()
                {
                    position.col
                }); //if no current key for that row add it
                else  validColumnsForRow[position.row].Add(position.col);
            }
        }
        public void Iterate()
        {
            double rng = RAND.NextDouble();
            bool evenrow = POSITION.row % 2 == 0;
            if (POSITION.col < 2)
            {
                rng -= VACUUM;
            }
            else if (POSITION.col > 4)
            {
                rng += VACUUM;
            }
            switch (rng)
            {
                case >= 0.5:
                    if (validColumnsForRow[POSITION.row+1].Contains(POSITION.col-1)) POSITION.col--;
                    break;
                    
                case < 0.5:
                    if (validColumnsForRow[POSITION.row+1].Contains(POSITION.col+1)) POSITION.col++;
                    break;
                default:
                    throw new Exception("generated an incorrect number");
            }

            POSITION.row++;
        }
    }

    
}
