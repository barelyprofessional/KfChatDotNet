using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Wheel game.
///
/// Wheel game rules:
/// - 20 elements on the wheel
/// - Three difficulty levels with different symbol distributions and payouts
/// - Low: mostly safe (white 1.2x, green 1.5x, black 0x)
/// - Medium: mixed (white 1.8x, yellow 2.0x, purple 3.0x, green 1.5x, black 0x)
/// - High: extreme (red 19.8x, black 0x)
/// </summary>
public class WheelRtpTests
{
    private const int Iterations = 100_000;
    private const decimal Wager = 100m;

    // Wheel configurations
    private const string LowDifficultyWheel = "🟢⚪⚪⚪⚫⚪⚪⚪⚪⚫🟢⚪⚪⚪⚫⚪⚪⚪⚪⚫";
    private const string MediumDifficultyWheel = "🟢⚫🟡⚫🟡⚫🟡⚫🟢⚫🟣⚫⚪⚫🟡⚫🟡⚫🟡⚫";
    private const string HighDifficultyWheel = "⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫⚫🔴";

    private static readonly Dictionary<string, decimal> LowDiffMultis = new()
    {
        { "⚫", 0.00m },
        { "⚪", 1.20m },
        { "🟢", 1.50m }
    };

    private static readonly Dictionary<string, decimal> MediumDiffMultis = new()
    {
        { "⚫", 0.00m },
        { "🟢", 1.50m },
        { "⚪", 1.80m },
        { "🟡", 2.00m },
        { "🟣", 3.00m }
    };

    private static readonly Dictionary<string, decimal> HighDiffMultis = new()
    {
        { "⚫", 0.00m },
        { "🔴", 19.80m }
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

    private static List<string> ExtractTextElements(string rawWheel)
    {
        var elements = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(rawWheel);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }
        return elements;
    }

    [Fact]
    public void Wheel_LowDifficulty_RTP_ShouldBeCalculated()
    {
        var wheelElements = ExtractTextElements(LowDifficultyWheel);
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var targetIndex = GetRandomNumber(0, wheelElements.Count - 1);
            var target = wheelElements[targetIndex];
            var multi = LowDiffMultis[target];

            if (multi > 0)
            {
                totalReturned += Wager * multi;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Count symbols for expected RTP calculation
        var symbolCounts = wheelElements.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"Wheel Low Difficulty Symbol Distribution:");
        foreach (var kvp in symbolCounts)
        {
            var multi = LowDiffMultis[kvp.Key];
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}/20 ({kvp.Value * 5}%) -> {multi}x");
        }
        Console.WriteLine($"Wheel Low RTP: {rtp:F2}% over {Iterations:N0} iterations");

        Assert.InRange(rtp, 80.0, 130.0);
    }

    [Fact]
    public void Wheel_MediumDifficulty_RTP_ShouldBeCalculated()
    {
        var wheelElements = ExtractTextElements(MediumDifficultyWheel);
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var targetIndex = GetRandomNumber(0, wheelElements.Count - 1);
            var target = wheelElements[targetIndex];
            var multi = MediumDiffMultis[target];

            if (multi > 0)
            {
                totalReturned += Wager * multi;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        var symbolCounts = wheelElements.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"Wheel Medium Difficulty Symbol Distribution:");
        foreach (var kvp in symbolCounts)
        {
            var multi = MediumDiffMultis[kvp.Key];
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}/20 ({kvp.Value * 5}%) -> {multi}x");
        }
        Console.WriteLine($"Wheel Medium RTP: {rtp:F2}% over {Iterations:N0} iterations");

        Assert.InRange(rtp, 70.0, 130.0);
    }

    [Fact]
    public void Wheel_HighDifficulty_RTP_ShouldBeCalculated()
    {
        var wheelElements = ExtractTextElements(HighDifficultyWheel);
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var targetIndex = GetRandomNumber(0, wheelElements.Count - 1);
            var target = wheelElements[targetIndex];
            var multi = HighDiffMultis[target];

            if (multi > 0)
            {
                totalReturned += Wager * multi;
            }
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        var symbolCounts = wheelElements.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"Wheel High Difficulty Symbol Distribution:");
        foreach (var kvp in symbolCounts)
        {
            var multi = HighDiffMultis[kvp.Key];
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}/20 ({kvp.Value * 5}%) -> {multi}x");
        }
        Console.WriteLine($"Wheel High RTP: {rtp:F2}% over {Iterations:N0} iterations");

        // High difficulty: 1/20 chance of 19.8x = 99% theoretical RTP
        Assert.InRange(rtp, 80.0, 120.0);
    }

    [Fact]
    public void Wheel_SymbolDistribution_ShouldBeConsistent()
    {
        Assert.Equal(20, ExtractTextElements(LowDifficultyWheel).Count);
        Assert.Equal(20, ExtractTextElements(MediumDifficultyWheel).Count);
        Assert.Equal(20, ExtractTextElements(HighDifficultyWheel).Count);
    }

    [Fact]
    public void Wheel_TheoreticalRTP_Calculation()
    {
        // Low Difficulty: 14 white (1.2x), 2 green (1.5x), 4 black (0x)
        var lowExpected = (14.0 / 20 * 1.2) + (2.0 / 20 * 1.5) + (4.0 / 20 * 0);
        Console.WriteLine($"Low Difficulty Theoretical RTP: {lowExpected * 100:F2}%");

        // Medium Difficulty: Calculate from actual wheel
        var medElements = ExtractTextElements(MediumDifficultyWheel);
        var medCounts = medElements.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        double medExpected = 0;
        foreach (var kvp in medCounts)
        {
            medExpected += (double)kvp.Value / 20 * (double)MediumDiffMultis[kvp.Key];
        }
        Console.WriteLine($"Medium Difficulty Theoretical RTP: {medExpected * 100:F2}%");

        // High Difficulty: 1 red (19.8x), 19 black (0x)
        var highExpected = (1.0 / 20 * 19.8) + (19.0 / 20 * 0);
        Console.WriteLine($"High Difficulty Theoretical RTP: {highExpected * 100:F2}%");
    }
}
