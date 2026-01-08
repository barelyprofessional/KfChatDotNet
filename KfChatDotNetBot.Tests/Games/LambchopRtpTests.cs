using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Lambchop game.
///
/// Game rules:
/// - 16-tile field
/// - Player targets 1-16 tiles
/// - Bot randomly places 0 or 1 "death tile"
/// - Win: Reach target without hitting death tile
/// - Multipliers vary by tile (1.072x to 20.37x)
/// - House edge: 0.015 (1.5%)
/// </summary>
public class LambchopRtpTests
{
    private const int Iterations = 50_000;
    private const decimal Wager = 100m;
    private const double HouseEdge = 0.015;
    private const int FieldLength = 16;

    private static readonly List<double> LambChopMultis =
    [
        1.072, 1.191, 1.331, 1.498, 1.698, 1.940, 2.238, 2.612, 3.086,
        3.704, 4.527, 5.658, 7.275, 9.700, 13.580, 20.370
    ];

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

    private static double GetRandomDouble()
    {
        var random = RandomShim.Create(StandardRng.Create());
        double result = 0;
        for (int i = 0; i < 10; i++)
        {
            result = random.NextDouble();
        }
        return result;
    }

    /// <summary>
    /// Replicates the death tile calculation logic from the game
    /// </summary>
    private static int CalculateDeathTile(int targetTile)
    {
        if (targetTile == FieldLength)
        {
            // Player wants to move all tiles
            double successChance = 1.0 / (FieldLength + 1);
            if (HouseEdge > 0)
            {
                successChance *= (1.0 - HouseEdge);
            }

            if (GetRandomDouble() <= successChance)
            {
                return -1; // No death tile (player succeeds)
            }
            else
            {
                double riggingFactor = GetRandomDouble();
                if (HouseEdge > 0 && riggingFactor < HouseEdge * 2)
                {
                    int minDeathTile = Math.Max(0, FieldLength - 3);
                    return GetRandomNumber(minDeathTile, FieldLength);
                }
                else
                {
                    return GetRandomNumber(0, FieldLength);
                }
            }
        }

        // Tiles 1-15
        if (HouseEdge < 0.015)
        {
            return GetRandomNumber(-1, FieldLength);
        }

        // Game is rigged
        int fairDeathTile = GetRandomNumber(-1, FieldLength);
        int tempFairDeathTile = fairDeathTile == -1 ? FieldLength + 1 : fairDeathTile;
        bool wouldSucceedFairly = tempFairDeathTile > targetTile;
        fairDeathTile = tempFairDeathTile == FieldLength + 1 ? -1 : fairDeathTile;

        if (wouldSucceedFairly)
        {
            double riggedFailChance = HouseEdge * 2;
            if (GetRandomDouble() <= riggedFailChance)
            {
                double cruelnessLevel = GetRandomDouble();
                if (cruelnessLevel < HouseEdge * 2)
                {
                    return targetTile > 1 ? targetTile - 1 : 1;
                }
                else
                {
                    return GetRandomNumber(-1, targetTile);
                }
            }
            return fairDeathTile;
        }
        else
        {
            double riggingFactor = GetRandomDouble();
            if (riggingFactor < HouseEdge)
            {
                int minTile = Math.Max(0, targetTile - 3);
                return GetRandomNumber(minTile, targetTile);
            }
            return fairDeathTile;
        }
    }

    [Theory]
    [InlineData(1)]   // Minimal risk
    [InlineData(8)]   // Medium risk
    [InlineData(16)]  // Maximum risk
    public void Lambchop_RTP_ShouldBeCalculated(int targetTile)
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            int deathTile = CalculateDeathTile(targetTile);

            bool win;
            if (deathTile == -1)
            {
                win = true;
            }
            else
            {
                win = (targetTile - 1) < deathTile;
            }

            if (win)
            {
                var multi = (decimal)LambChopMultis[targetTile - 1];
                totalReturned += Wager * multi;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        Console.WriteLine($"Lambchop RTP for target tile {targetTile}: {rtp:F2}% over {Iterations:N0} iterations");
        Console.WriteLine($"  Multiplier: {LambChopMultis[targetTile - 1]}x");

        Assert.InRange(rtp, 50.0, 150.0);
    }

    [Fact]
    public void Lambchop_WinRate_ShouldDecreaseWithRisk()
    {
        var winRates = new Dictionary<int, double>();

        foreach (int targetTile in new[] { 1, 4, 8, 12, 16 })
        {
            int wins = 0;

            for (int i = 0; i < Iterations; i++)
            {
                int deathTile = CalculateDeathTile(targetTile);
                bool win = deathTile == -1 || (targetTile - 1) < deathTile;

                if (win) wins++;
            }

            winRates[targetTile] = (double)wins / Iterations;
        }

        Console.WriteLine("Lambchop Win Rates by Target Tile:");
        foreach (var kvp in winRates.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  Tile {kvp.Key}: {kvp.Value * 100:F2}% win rate");
        }

        // Higher target tiles should generally have lower win rates
        Assert.True(winRates[1] > winRates[16], "Tile 1 should have higher win rate than tile 16");
    }

    [Fact]
    public void Lambchop_Multipliers_ShouldBeValid()
    {
        Assert.Equal(FieldLength, LambChopMultis.Count);

        // All multipliers should be > 1
        foreach (var multi in LambChopMultis)
        {
            Assert.True(multi > 1.0, $"Multiplier {multi} should be > 1");
        }

        // Multipliers should increase with risk
        for (int i = 1; i < LambChopMultis.Count; i++)
        {
            Assert.True(LambChopMultis[i] > LambChopMultis[i - 1],
                $"Multiplier at {i} ({LambChopMultis[i]}) should be > multiplier at {i - 1} ({LambChopMultis[i - 1]})");
        }

        Console.WriteLine("Lambchop Multiplier Table:");
        for (int i = 0; i < LambChopMultis.Count; i++)
        {
            Console.WriteLine($"  Tile {i + 1}: {LambChopMultis[i]}x");
        }
    }

    [Fact]
    public void Lambchop_TheoreticalRTP_Estimate()
    {
        // For each tile, estimate theoretical RTP
        Console.WriteLine("Lambchop Theoretical RTP Estimates:");
        for (int targetTile = 1; targetTile <= 16; targetTile++)
        {
            // Fair win probability (no house edge): targetTile / (FieldLength + 1)
            // With house edge: adjusted probability
            double fairWinProbability = (double)(FieldLength + 1 - targetTile) / (FieldLength + 1);
            double adjustedWinProbability = fairWinProbability * (1 - HouseEdge);
            double multi = LambChopMultis[targetTile - 1];
            double theoreticalRtp = adjustedWinProbability * multi;

            Console.WriteLine($"  Tile {targetTile}: {theoreticalRtp * 100:F2}% (win prob: {adjustedWinProbability * 100:F2}%, multi: {multi}x)");
        }
    }
}
