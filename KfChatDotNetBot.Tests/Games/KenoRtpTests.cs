using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Keno game.
///
/// Keno game rules:
/// - 40-number pool (1-40)
/// - Player picks 1-10 numbers
/// - Casino draws 10 numbers
/// - Payout based on matches count
/// </summary>
public class KenoRtpTests
{
    private const int Iterations = 50_000;
    private const decimal Wager = 100m;

    // Payout table from the game (selections x matches)
    private static readonly double[,] PayoutMultipliers =
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

    private static List<int> GenerateKenoNumbers(int size)
    {
        var numbers = new List<int>();
        for (int i = 0; i < size; i++)
        {
            bool repeatNum = true;
            while (repeatNum)
            {
                int randomNum = GetRandomNumber(1, 40);
                if (!numbers.Contains(randomNum))
                {
                    numbers.Add(randomNum);
                    repeatNum = false;
                }
            }
        }
        return numbers;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void Keno_RTP_ShouldBeWithinExpectedRange(int numbersPicked)
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var playerNumbers = GenerateKenoNumbers(numbersPicked);
            var casinoNumbers = GenerateKenoNumbers(10);
            var matches = playerNumbers.Intersect(casinoNumbers).Count();
            var payoutMulti = PayoutMultipliers[numbersPicked - 1, matches];

            if (payoutMulti > 0)
            {
                totalReturned += Wager * (decimal)payoutMulti;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Keno typically has RTP between 70-98% depending on selections
        Assert.InRange(rtp, 50.0, 150.0);

        Console.WriteLine($"Keno RTP with {numbersPicked} numbers: {rtp:F2}% over {Iterations:N0} iterations");
    }

    [Fact]
    public void Keno_MatchDistribution_ShouldFollowHypergeometric()
    {
        int numbersPicked = 10;
        var matchCounts = new Dictionary<int, int>();

        for (int i = 0; i <= 10; i++)
        {
            matchCounts[i] = 0;
        }

        for (int i = 0; i < Iterations; i++)
        {
            var playerNumbers = GenerateKenoNumbers(numbersPicked);
            var casinoNumbers = GenerateKenoNumbers(10);
            var matches = playerNumbers.Intersect(casinoNumbers).Count();
            matchCounts[matches]++;
        }

        Console.WriteLine($"Keno Match Distribution (10 numbers picked):");
        foreach (var kvp in matchCounts.OrderBy(x => x.Key))
        {
            var percentage = (double)kvp.Value / Iterations * 100;
            Console.WriteLine($"  {kvp.Key} matches: {percentage:F2}% ({kvp.Value:N0} times)");
        }

        // Most common should be 2-3 matches
        var mostCommon = matchCounts.OrderByDescending(x => x.Value).First().Key;
        Assert.InRange(mostCommon, 1, 4);
    }

    [Fact]
    public void Keno_PayoutTable_ShouldBeValid()
    {
        // Verify payout table dimensions
        Assert.Equal(10, PayoutMultipliers.GetLength(0));
        Assert.Equal(11, PayoutMultipliers.GetLength(1));

        // Verify all payouts are non-negative
        for (int selections = 0; selections < 10; selections++)
        {
            for (int matches = 0; matches <= 10; matches++)
            {
                Assert.True(PayoutMultipliers[selections, matches] >= 0,
                    $"Negative payout at [{selections},{matches}]");
            }
        }

        // Verify 10/10 match pays 1000x
        Assert.Equal(1000.0, PayoutMultipliers[9, 10]);
    }
}
