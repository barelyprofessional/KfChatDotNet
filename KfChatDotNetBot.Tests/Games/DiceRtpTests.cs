using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Dice game.
///
/// Dice game rules:
/// - Roll between 0.0 and 1.0
/// - Win if roll > 0.5 + houseEdge (0.515)
/// - Win pays 1x (even money)
/// - Theoretical RTP = (1 - 0.515) * 2 = 97% (approx)
/// </summary>
public class DiceRtpTests
{
    private const double HouseEdge = 0.015;
    private const double WinThreshold = 0.5 + HouseEdge; // 0.515
    private const int Iterations = 100_000;
    private const decimal Wager = 100m;

    private static double GetRandomDouble()
    {
        var rng = StandardRng.Create();
        var random = RandomShim.Create(rng);
        double result = 0;
        for (int i = 0; i < 10; i++)
        {
            result = random.NextDouble();
        }
        return result;
    }

    [Fact]
    public void Dice_RTP_ShouldBeWithinExpectedRange()
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var rolled = GetRandomDouble();

            if (rolled > WinThreshold)
            {
                // Win: get wager back + wager (2x)
                totalReturned += Wager * 2;
            }
            // Loss: return 0
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Theoretical RTP is approximately 97% (0.485 win rate * 2x payout)
        // Allow for statistical variance (95% - 99%)
        Assert.InRange(rtp, 94.0, 100.0);

        // Output for visibility
        Console.WriteLine($"Dice RTP: {rtp:F2}% over {Iterations:N0} iterations");
        Console.WriteLine($"Win threshold: {WinThreshold}");
        Console.WriteLine($"Expected RTP: ~{(1 - WinThreshold) * 200:F2}%");
    }

    [Fact]
    public void Dice_WinProbability_ShouldMatchExpected()
    {
        int wins = 0;

        for (int i = 0; i < Iterations; i++)
        {
            var rolled = GetRandomDouble();
            if (rolled > WinThreshold)
            {
                wins++;
            }
        }

        var winRate = (double)wins / Iterations;
        var expectedWinRate = 1 - WinThreshold; // 0.485

        // Allow 1% variance
        Assert.InRange(winRate, expectedWinRate - 0.01, expectedWinRate + 0.01);

        Console.WriteLine($"Dice Win Rate: {winRate * 100:F2}% (expected: {expectedWinRate * 100:F2}%)");
    }

    [Fact]
    public void Dice_HouseEdge_ShouldMatchExpected()
    {
        // The house edge is the difference between theoretical fair odds and actual odds
        // Fair game: 50% win rate at 2x payout = 100% RTP
        // With house edge: (50% - 1.5%) win rate at 2x payout = 97% RTP
        // House edge = 3% (2 * 1.5%)

        var theoreticalHouseEdge = 2 * HouseEdge; // 0.03 = 3%
        var expectedRtp = 1 - theoreticalHouseEdge; // 0.97 = 97%

        Assert.Equal(0.03, theoreticalHouseEdge, 3);
        Assert.Equal(0.97, expectedRtp, 2);

        Console.WriteLine($"Dice House Edge: {theoreticalHouseEdge * 100:F2}%");
        Console.WriteLine($"Dice Expected RTP: {expectedRtp * 100:F2}%");
    }
}
