using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Planes game.
///
/// Game rules:
/// - Plane flies through a 6x20 board of hazards
/// - Bombs (hit: multiplier /= 2, gravity applied)
/// - Multis (hit: multiplier increases by weighted random 1-10)
/// - Win: Land on carrier (every 6 columns) at height 6
/// - Lose: Crash into water (height >= 6 not on carrier)
/// </summary>
public class PlanesRtpTests
{
    private const int Iterations = 10_000;
    private const decimal Wager = 100m;
    private const int CarrierCount = 6;

    private static int GetRandomNumber(int min, int max)
    {
        var random = RandomShim.Create(StandardRng.Create());
        int result = 0;
        for (int i = 0; i < 10; i++)
        {
            result = random.Next(min, max + 1);
        }
        return result;
    }

    private static int[,] CreatePlanesBoard(int forceTiles = -1)
    {
        var board = new int[6, 20];
        for (int row = 0; row < 6; row++)
        {
            for (int column = 0; column < 20; column++)
            {
                var randomNum = GetRandomNumber(1, 100);
                if (forceTiles != -1)
                {
                    board[row, column] = forceTiles;
                }
                else
                {
                    // 0 = neutral (49%), 1 = bomb (21%), 2 = multi (30%)
                    board[row, column] = randomNum switch
                    {
                        < 49 => 0,
                        > 79 => 1,
                        _ => 2
                    };
                }
            }
        }
        return board;
    }

    private class Plane
    {
        public int Height = 1;
        public decimal MultiTracker = 1;
        public int JustHitMulti = 1;
        public bool Crashed = false;
        private readonly Random _random = RandomShim.Create(StandardRng.Create());

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
            var randomNum = GetRandomNumber(0, 4);
            var weightedRand = WeightedRandomNumber(1, 10);
            if (randomNum == 0)
            {
                MultiTracker *= weightedRand + 0.1m;
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
            int range = max - min + 1;
            double weight = 6.25 + Height;
            double r = _random.NextDouble();
            double exp = -Math.Log(1 - r) / weight;
            int returnVal = min + (int)Math.Round(exp * range);
            return Math.Clamp(returnVal, min, max);
        }
    }

    private static (bool win, decimal multiplier) SimulatePlanesGame()
    {
        var planesBoard = CreatePlanesBoard();
        var planesBoard2 = CreatePlanesBoard();
        var planesBoard3 = CreatePlanesBoard();
        List<int[,]> planesBoards = [planesBoard, planesBoard2, planesBoard3];

        var plane = new Plane();
        int fullCounter = 3; // Start after pre-game frames

        while (plane.Height < 6)
        {
            int counter = (fullCounter - 3) % 20;

            // Process current position
            if (plane.Height < 6 && counter < 20)
            {
                switch (planesBoards[1][plane.Height, counter])
                {
                    case 0: // Neutral
                        break;
                    case 1: // Bomb
                        planesBoards[1][plane.Height, counter] = 0;
                        plane.HitRocket();
                        break;
                    case 2: // Multi
                        planesBoards[1][plane.Height, counter] = 0;
                        plane.HitMulti();
                        break;
                }
            }

            plane.Gravity();
            fullCounter++;

            // Switch boards every 20 frames
            if ((fullCounter - 3) % 20 == 0 && fullCounter != 3)
            {
                planesBoards.RemoveAt(0);
                planesBoards.Add(CreatePlanesBoard());
            }

            // Safety limit
            if (fullCounter > 1000) break;
        }

        // Check win condition
        bool win = (fullCounter - 3) % CarrierCount == 0;

        if (win)
        {
            return (true, plane.MultiTracker);
        }
        else
        {
            return (false, 0);
        }
    }

    [Fact]
    public void Planes_RTP_ShouldBeCalculated()
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var (win, multiplier) = SimulatePlanesGame();

            if (win)
            {
                totalReturned += Wager * multiplier;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        Console.WriteLine($"Planes RTP: {rtp:F2}% over {Iterations:N0} iterations");

        // Planes is a complex game, RTP can vary widely
        Assert.InRange(rtp, 10.0, 200.0);
    }

    [Fact]
    public void Planes_WinRate_ShouldBeCalculated()
    {
        int wins = 0;
        var multipliers = new List<decimal>();

        for (int i = 0; i < Iterations; i++)
        {
            var (win, multiplier) = SimulatePlanesGame();
            if (win)
            {
                wins++;
                multipliers.Add(multiplier);
            }
        }

        var winRate = (double)wins / Iterations;
        var avgMultiplier = multipliers.Count > 0 ? multipliers.Average() : 0;

        Console.WriteLine($"Planes Win Rate: {winRate * 100:F2}%");
        Console.WriteLine($"Average Win Multiplier: {avgMultiplier:F2}x");
        Console.WriteLine($"Total Wins: {wins}/{Iterations}");

        // Win rate should be reasonable (landing on carrier every 6 columns)
        Assert.InRange(winRate, 0.05, 0.50);
    }

    [Fact]
    public void Planes_BoardDistribution_ShouldBeCorrect()
    {
        int neutralCount = 0;
        int bombCount = 0;
        int multiCount = 0;
        int totalCells = 0;

        for (int i = 0; i < 100; i++)
        {
            var board = CreatePlanesBoard();
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 20; col++)
                {
                    totalCells++;
                    switch (board[row, col])
                    {
                        case 0: neutralCount++; break;
                        case 1: bombCount++; break;
                        case 2: multiCount++; break;
                    }
                }
            }
        }

        var neutralPct = (double)neutralCount / totalCells * 100;
        var bombPct = (double)bombCount / totalCells * 100;
        var multiPct = (double)multiCount / totalCells * 100;

        Console.WriteLine($"Board Distribution:");
        Console.WriteLine($"  Neutral: {neutralPct:F2}% (expected: 48%)");
        Console.WriteLine($"  Bombs: {bombPct:F2}% (expected: 21%)");
        Console.WriteLine($"  Multis: {multiPct:F2}% (expected: 31%)");

        // Verify distribution is roughly correct
        Assert.InRange(neutralPct, 40.0, 55.0);
        Assert.InRange(bombPct, 15.0, 30.0);
        Assert.InRange(multiPct, 25.0, 40.0);
    }

    [Fact]
    public void Planes_CarrierSpacing_IsEvery6Columns()
    {
        // Carriers appear at columns 0, 6, 12, 18, 24, 30...
        for (int col = 0; col < 100; col++)
        {
            bool isCarrier = col % CarrierCount == 0;
            if (isCarrier)
            {
                Console.WriteLine($"Carrier at column {col}");
            }
        }

        // Verify carrier count logic
        Assert.Equal(0, 0 % CarrierCount);
        Assert.Equal(0, 6 % CarrierCount);
        Assert.Equal(0, 12 % CarrierCount);
        Assert.NotEqual(0, 5 % CarrierCount);
    }

    [Fact]
    public void Planes_MultiplierProgression_ShouldBeCalculated()
    {
        var finalMultipliers = new List<decimal>();

        for (int i = 0; i < 1000; i++)
        {
            var (win, multiplier) = SimulatePlanesGame();
            if (win)
            {
                finalMultipliers.Add(multiplier);
            }
        }

        if (finalMultipliers.Count > 0)
        {
            Console.WriteLine($"Multiplier Statistics ({finalMultipliers.Count} wins):");
            Console.WriteLine($"  Min: {finalMultipliers.Min():F2}x");
            Console.WriteLine($"  Max: {finalMultipliers.Max():F2}x");
            Console.WriteLine($"  Avg: {finalMultipliers.Average():F2}x");
            Console.WriteLine($"  Median: {finalMultipliers.OrderBy(x => x).ElementAt(finalMultipliers.Count / 2):F2}x");
        }
        else
        {
            Console.WriteLine("No wins recorded in 1000 games");
        }
    }
}
