using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Limbo game.
///
/// Limbo game rules:
/// - Player picks a target multiplier (default 2x)
/// - Game generates a weighted random number
/// - Win if generated number >= target multiplier
/// - Win pays (multiplier - 1) * wager
/// - Theoretical RTP is approximately 1/multiplier win rate
/// </summary>
public class LimboRtpTests
{
    private const int Iterations = 100_000;
    private const decimal Wager = 100m;
    private const double Min = 1;
    private const double Max = 10000;

    /// <summary>
    /// Replicates the limbo game's weighted random number generation
    /// </summary>
    private static decimal[] Get1XWeightedRandomNumber(double minValue, double maxValue, decimal multi)
    {
        var random = RandomShim.Create(StandardRng.Create());
        var skew = 1.0 / (double)(multi * 1.01m);
        var gamma = Math.Log(0.5) / Math.Log(skew);
        var r = random.NextDouble();
        var rP = 1 - Math.Pow(1 - r, gamma);
        var lnMin = Math.Log(minValue);
        var lnMax = Math.Log(maxValue);
        var exponent = lnMin + rP * (lnMax - lnMin);
        var result = new decimal[2];
        result[0] = (decimal)Math.Exp(exponent);
        result[1] = GetScaledNumber(lnMin, lnMax, exponent, result[0], multi);
        return result;
    }

    private static decimal GetScaledNumber(double lnMin, double lnMax, double exponent, decimal result, decimal multi)
    {
        var anchor = Math.Log((double)multi);
        var deltaMax = lnMax - anchor;
        var k = Math.Log(Max / (double)(multi * multi)) / deltaMax;
        var delta = exponent - anchor;
        var logFactor = k * delta;
        var factor = Math.Exp(logFactor);
        var preResult = result * (decimal)factor;

        if (!((double)preResult < anchor)) return preResult;

        var minTheo = (double)(multi * multi) / Max;
        var logMinTheo = Math.Log(minTheo);
        var logPreResult = Math.Log((double)preResult);
        var fraction = (logPreResult - logMinTheo) / (anchor - logMinTheo);
        return (decimal)Math.Exp(anchor * fraction);
    }

    [Theory]
    [InlineData(2.0)]    // Default multiplier
    [InlineData(1.5)]    // Low risk
    [InlineData(5.0)]    // Medium risk
    [InlineData(10.0)]   // High risk
    public void Limbo_RTP_ShouldBeWithinExpectedRange(double targetMultiplier)
    {
        var multi = (decimal)targetMultiplier;
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var casinoNumbers = Get1XWeightedRandomNumber(Min, (double)(multi * multi), multi);

            if (casinoNumbers[0] >= multi)
            {
                // Win: get wager * multiplier
                totalReturned += Wager * multi;
            }
            // Loss: return 0
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Limbo should have approximately 99% RTP (fair odds with 1% house edge from the 1.01 skew factor)
        Assert.InRange(rtp, 90.0, 110.0);

        Console.WriteLine($"Limbo RTP at {targetMultiplier}x: {rtp:F2}% over {Iterations:N0} iterations");
    }

    [Fact]
    public void Limbo_WinRate_AtDefaultMultiplier_ShouldBeApproximatelyHalf()
    {
        decimal multi = 2m;
        int wins = 0;

        for (int i = 0; i < Iterations; i++)
        {
            var casinoNumbers = Get1XWeightedRandomNumber(Min, (double)(multi * multi), multi);
            if (casinoNumbers[0] >= multi)
            {
                wins++;
            }
        }

        var winRate = (double)wins / Iterations;
        // At 2x multiplier, win rate should be approximately 1/2 * (1/1.01) = ~49.5%
        var expectedWinRate = 1.0 / (double)multi / 1.01;

        // Allow 3% variance due to complex distribution
        Assert.InRange(winRate, expectedWinRate - 0.03, expectedWinRate + 0.03);

        Console.WriteLine($"Limbo Win Rate at 2x: {winRate * 100:F2}% (expected: ~{expectedWinRate * 100:F2}%)");
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(5.0)]
    public void Limbo_WinRate_ShouldScaleInverselyWithMultiplier(double targetMultiplier)
    {
        var multi = (decimal)targetMultiplier;
        int wins = 0;

        for (int i = 0; i < Iterations; i++)
        {
            var casinoNumbers = Get1XWeightedRandomNumber(Min, (double)(multi * multi), multi);
            if (casinoNumbers[0] >= multi)
            {
                wins++;
            }
        }

        var winRate = (double)wins / Iterations;
        // Expected: approximately 1/multiplier with 1% reduction from skew
        var expectedWinRate = 1.0 / targetMultiplier / 1.01;

        Console.WriteLine($"Limbo Win Rate at {targetMultiplier}x: {winRate * 100:F2}% (expected: ~{expectedWinRate * 100:F2}%)");

        // Allow 5% variance for higher multipliers
        Assert.InRange(winRate, expectedWinRate - 0.05, expectedWinRate + 0.05);
    }
}
