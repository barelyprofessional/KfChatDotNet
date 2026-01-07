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
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    
    public RateLimitOptionsModel? RateLimitOptions => null;

    private string NULLSPACE = "⚫";
    private string EMPTYSPACE = "⚪";
    private string BALL = "🟠";
    private int DIFFICULTY = 7;//maybe plan to allow user to change difficulty of plinko in future updates, would need to change the payout logic though

    private readonly Dictionary<int, decimal> PlinkoPayoutBoard = new()
    {
        {0, 25},
        {1, 2_5},
        {2, 0_25},
        {3, 0_1},
        {4, 0_25},
        {5, 2_5},
        {6, 25},
        
    };
    
    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        decimal payout = 0;
        decimal currentPayout = 0;
        Group? number;
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoPlinkoCleanupDelay, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
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
        if (!arguments.TryGetValue("number", out number))
        {
            number = null;
        }
        int numberOfBalls = number == null ? 1 : Convert.ToInt32(number.Value);
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
        int breakCounter = 0
        var plinkoMessageID = await botInstance.SendChatMessageAsync(PlinkoBoardDisplay(ballsInPlay), true, autoDeleteAfter: cleanupDelay);
        while (plinkoMessageID.ChatMessageId == null && breakCounter < 1000) { 
            await Task.Delay(100);
            breakCounter++;
        }
        if (breakCounter >= 999){
            throw new Exception("game broke while waiting for chat message id");
        }
        breakCounter = 0;
        while (ballsNotInPlay.Count > 0 || ballsInPlay.Count > 0)
        {
            breakCounter++;
            if (breakCounter >= 1000) throw new Exception("stuck in while loop in plinko");
            currentPayout = 0;
            ballsInPlay.Add(ballsNotInPlay[0]);
            ballsNotInPlay.RemoveAt(0);
            await botInstance.KfClient.EditMessageAsync(plinkoMessageID.ChatMessageId!.Value,PlinkoBoardDisplay(ballsInPlay));
            if (ballsInPlay[0].POSITION.row == DIFFICULTY - 1) //once your ball has reached the bottom calculate the payout
            {
                currentPayout = wager * PlinkoPayoutBoard[ballsInPlay[0].POSITION.col];
                ballsInPlay.RemoveAt(0);
                if (currentPayout > wager)
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value!}]win![/color]. Payout: {currentPayout} KKK", true, autoDeleteAfter: TimeSpan.FromSeconds(5));
                }
                else
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsRedColor].Value!}lose. Payout: {currentPayout} KKK", true, autoDeleteAfter: TimeSpan.FromSeconds(5));
                }
            }
            foreach (var ball in ballsInPlay)
            {
                ball.Iterate();
            }

            await Task.Delay(100);

        }
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager*numberOfBalls, payout, WagerGame.Plinko, ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you won ${payout} KKK from {numberOfBalls} plinko balls worth ${wager} KKK. Balance: ${newBalance} KKK", true, autoDeleteAfter: cleanupDelay);
        
    }

    public string PlinkoBoardDisplay(List<PlinkoBall> balls)
    {
        string board = "";
        bool spaceIsBall = false;
        bool spaceIsValid = false;
        List<(int row, int col)> validPositions = new() //would need to come up with a formula to make this to have user defined difficulty, good luck
        {
            (0, 3),
            (1, 2), (1, 4),
            (2, 2), (2, 3), (2, 4),
            (3, 1), (3,2), (3, 4), (3, 5),
            (4, 1), (4, 2), (4, 3), (4, 4), (4, 5),
            (5, 0), (5, 1), (5, 2), (5, 4), (5, 5), (5, 6),
            (6, 0), (6, 1), (6, 2), (6, 3), (6, 4), (6, 5), (6,6)
        };
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
        }

        return board;
    }
    public class PlinkoBall
    {
        private RandomShim<StandardRng> RAND = RandomShim.Create(StandardRng.Create());
        public (int row, int col) POSITION;

        public PlinkoBall()
        {
            POSITION = (0, 0);
            
        }
        public void Iterate()
        {
            int rng = RAND.Next(2);
            bool evenrow = POSITION.row % 2 == 0;
            switch (rng)
            {
                case 0:
                    if (!evenrow && Math.Abs(POSITION.col) > POSITION.row / 2) POSITION.col--;
                    break;
                case 1:
                    if (!evenrow && POSITION.col > POSITION.row / 2) POSITION.col++;
                    break;
                default:
                    throw new Exception("generated an incorrect number");
            }

            POSITION.row++;
        }
    }

    
}

