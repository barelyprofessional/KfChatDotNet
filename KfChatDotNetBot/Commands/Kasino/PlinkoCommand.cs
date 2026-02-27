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
        new Regex(@"^plinko (?<amount>\d+(?:\.\d+)?) (?<number>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^plinko (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
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

    private const string NULLSPACE =   "⚫";
    private const string EMPTYSPACE =  "⚪";
    private const string BALL =        "🟠";
    private const string LOSESPACE =   "🔻";
    private const string SMALLWINSPACE = "🟢";
    private const string MIDWINSPACE = "🍀";
    private const string BIGWINSPACE = "💲";
    
    private const int DIFFICULTY = 8;//maybe plan to allow user to change difficulty of plinko in future updates, would need to change the payout logic though
    private static double VACUUM = 0.25;
    private decimal HOUSE_EDGE = (decimal)0.98;
    
    private static Dictionary<decimal, string> PAYOUTSTOSTRING = new Dictionary<decimal, string>()
    {
        {69, BIGWINSPACE},
        {(decimal)42.069, MIDWINSPACE},
        {9, SMALLWINSPACE},
        {(decimal)0.1, LOSESPACE}
    };
    
    private static readonly Dictionary<int, decimal> PlinkoPayoutBoard = new()
    {
        {0, 69},
        {2, (decimal)42.069},
        {4, 9},
        {6, (decimal)0.1},
        {8, (decimal)0.1},
        {10, 9},
        {12, (decimal)42.069},
        {14, 69}
        
    };

    private static List<(int row, int col)> validPositions = [];
    
    private static Dictionary<int, List<int>> validColumnsForRow = new();
    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        VACUUM += 1 - (double)HOUSE_EDGE;
        validPositions = new List<(int row, int col)>() { (0, DIFFICULTY-1) };
        validColumnsForRow = new Dictionary<int, List<int>>(){{0, new List<int>(){DIFFICULTY-1}}};
        
        //calculate all the valid positions for the difficulty
        for (int i = 1; i < DIFFICULTY; i++)
        {
            // Find all positions from the row we just finished (i-1)
            var previousRowPositions = validPositions.Where(p => p.row == i - 1).ToList();

            foreach (var pos in previousRowPositions)
            {
                var leftChild = (i, pos.col - 1);
                var rightChild = (i, pos.col + 1);

                // Use a hash-set or check Contains to avoid adding duplicate nodes
                if (!validPositions.Contains(leftChild)) validPositions.Add(leftChild);
                if (!validPositions.Contains(rightChild)) validPositions.Add(rightChild);
            }
        }
        
        //calculate all the valid columns for any particular row
        foreach (var position in validPositions)
        {
            if (!validColumnsForRow.ContainsKey(position.row)) validColumnsForRow.Add(position.row, new List<int>(){position.col});
            else validColumnsForRow[position.row].Add(position.col);
            
        }
        
        
        
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
        /*if (wager > 1)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, plinko is currently limited to 1 KKK wagers while bugs are ironed out.", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }*/
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
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }
        if (gambler.Balance < wager * numberOfBalls)
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
            await Task.Delay(100, ctx);
            breakCounter++;
        }
        if (breakCounter >= 999){
            throw new Exception("game broke while waiting for chat message id");
        }
        breakCounter = 0;
        var logger = LogManager.GetCurrentClassLogger();
        string lastPayoutMessage = "";
        string PlinkoMessage = "";
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
            PlinkoMessage = PlinkoBoardDisplay(ballsInPlay) + "[br]" + lastPayoutMessage;
            await botInstance.KfClient.EditMessageAsync(plinkoMessageID.ChatMessageId!.Value,PlinkoMessage);
            if (ballsInPlay[0].POSITION.row == DIFFICULTY - 1) //once your ball has reached the bottom calculate the payout
            {
                currentPayout = wager * PlinkoPayoutBoard[ballsInPlay[0].POSITION.col];
                payout += currentPayout;
                if (currentPayout == wager * 25) logger.Info($"Plinko: Max win on plinko, ball position: ({ballsInPlay[0].POSITION.row}, {ballsInPlay[0].POSITION.col})");
                if (currentPayout > wager)
                {
                    lastPayoutMessage = ($"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value!}]won[/color] {await currentPayout.FormatKasinoCurrencyAsync()} from a plinko ball worth {await wager.FormatKasinoCurrencyAsync()}!");
                }
                else
                {
                    var lossDisplay = wager - currentPayout;
                    lastPayoutMessage = ($"{user.FormatUsername()}, you [color={settings[BuiltIn.Keys.KiwiFarmsRedColor].Value!}]lost[/color] {await lossDisplay.FormatKasinoCurrencyAsync()} from a plinko ball worth {await wager.FormatKasinoCurrencyAsync()}.");
                }
                ballsInPlay.RemoveAt(0);
            }
            foreach (var ball in ballsInPlay)
            {
                ball.Iterate();
            }

            await Task.Delay(300, ctx);
            PlinkoMessage = PlinkoBoardDisplay(ballsInPlay) + "[br]" + lastPayoutMessage;
            await botInstance.KfClient.EditMessageAsync(plinkoMessageID.ChatMessageId!.Value, PlinkoMessage);
            await Task.Delay(300, ctx);

        }
        var newBalance = await Money.NewWagerAsync(gambler.Id, wager*numberOfBalls, payout-(wager*numberOfBalls), WagerGame.Plinko, ct: ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, [u]you won {await payout.FormatKasinoCurrencyAsync()} from {numberOfBalls} plinko balls worth ${wager} KKK. Balance: {await newBalance.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: cleanupDelay);
        
    }

    public string PlinkoBoardDisplay(List<PlinkoBall> balls)
    {
        string board = "";
        bool spaceIsBall = false;
        bool spaceIsValid = false;
        
        for (int row = 0; row < DIFFICULTY; row++)
        {
            for (int col = 0; col < DIFFICULTY*2-1; col++)
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

                        if (!spaceIsBall)
                        {
                            if (row == DIFFICULTY - 1)
                            {
                                foreach (var num in new List<int>(){0,2,4,6,8,10,12,14}) 
                                    if (col == num) board += PAYOUTSTOSTRING[PlinkoPayoutBoard[num]];
                            }
                            else board += EMPTYSPACE;
                        }
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
        public PlinkoBall()
        {
            POSITION = validPositions[0];
            
        }
        public void Iterate()
        {
            double rng = RAND.NextDouble();
            bool evenrow = POSITION.row % 2 == 0;
            if (POSITION.col < DIFFICULTY-1)
            {
                rng -= VACUUM;
            }
            else if (POSITION.col > DIFFICULTY-1)
            {
                rng += VACUUM;
            }
            switch (rng)
            {
                case >= 0.5:
                    POSITION.col--;
                    break;
                    
                case < 0.5:
                    POSITION.col++;
                    break;
                default:
                    throw new Exception("generated an incorrect number");
            }

            POSITION.row++;
        }
    }

    
}
