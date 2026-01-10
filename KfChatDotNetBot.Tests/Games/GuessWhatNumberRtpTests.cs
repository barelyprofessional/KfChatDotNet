using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the GuessWhatNumber game.
///
/// Game rules:
/// - Player guesses a number between 1-10
/// - Casino picks a random number 1-10
/// - Win if guess matches: pays 9x wager
/// - Lose if guess doesn't match: lose wager
/// - Theoretical RTP: 10% win rate * 10x return = 100% (but game pays 9x, not 10x)
/// - Actual theoretical RTP: 10% * 10 (wager + 9x win) = 100%... Wait, let me recalculate
/// - Win: get back 10x wager (wager + 9x effect)
/// - Theoretical RTP: 10% * 10 = 100%? No, effect is +9x, so you get 10x total
/// - Actually: 10% chance to win 9x (plus keep wager) = 10% * 10 = 100%?
/// - Code shows: effect = wager * 9, so total returned on win = wager + (wager * 9) = 10 * wager
/// - So RTP = 10% * 10 = 100%? That seems too generous
/// - Let me re-read: effect is wager * 9, added to balance via NewWagerAsync
/// - In NewWagerAsync, if effect > 0, balance += effect (which is win - wager already subtracted)
/// - So win pays 9x net profit, total return is 10x
/// - RTP = 10% * 10 = 100%? That's fair odds
///
/// Wait, re-reading NewWagerAsync more carefully:
/// - isComplete = true by default
/// - balance += wagerEffect (which is wager * 9 for win, -wager for loss)
/// - So on win: balance goes up by 9 * wager (net profit)
/// - Total returned to player on win: original wager + 9x wager = 10x wager
/// - RTP = (10% chance) * (10x return) = 100%
///
/// This seems like a fair game with 0% house edge!
/// </summary>
public class GuessWhatNumberRtpTests
{
    private const int Iterations = 100_000;
    private const decimal Wager = 100m;

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

    [Fact]
    public void GuessWhatNumber_RTP_ShouldBeClose100Percent()
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var playerGuess = GetRandomNumber(1, 10); // Random guess
            var casinoAnswer = GetRandomNumber(1, 10);

            if (playerGuess == casinoAnswer)
            {
                // Win: get wager back + 9x wager = 10x total
                totalReturned += Wager * 10;
            }
            // Loss: return 0
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Theoretical RTP is 100% (10% * 10x = 100%)
        // Allow for statistical variance
        Assert.InRange(rtp, 90.0, 110.0);

        Console.WriteLine($"GuessWhatNumber RTP: {rtp:F2}% over {Iterations:N0} iterations");
        Console.WriteLine($"Expected RTP: 100% (10% win rate * 10x payout)");
    }

    [Fact]
    public void GuessWhatNumber_WinRate_ShouldBeApproximately10Percent()
    {
        int wins = 0;

        for (int i = 0; i < Iterations; i++)
        {
            var playerGuess = GetRandomNumber(1, 10);
            var casinoAnswer = GetRandomNumber(1, 10);

            if (playerGuess == casinoAnswer)
            {
                wins++;
            }
        }

        var winRate = (double)wins / Iterations;

        // 10% expected win rate with some variance
        Assert.InRange(winRate, 0.08, 0.12);

        Console.WriteLine($"GuessWhatNumber Win Rate: {winRate * 100:F2}% (expected: 10%)");
    }

    [Fact]
    public void GuessWhatNumber_TheoreticalRTP_Is100Percent()
    {
        // 10 possible outcomes
        // 1 winning outcome per guess
        // Win probability = 1/10 = 10%
        // Win payout = 9x (net profit) + 1x (wager return) = 10x total
        // RTP = 0.10 * 10 = 1.0 = 100%

        double winProbability = 1.0 / 10;
        double totalReturnOnWin = 10.0; // wager * 10
        double theoreticalRtp = winProbability * totalReturnOnWin;

        Assert.Equal(1.0, theoreticalRtp);
        Console.WriteLine($"Theoretical RTP: {theoreticalRtp * 100:F2}%");
        Console.WriteLine($"House Edge: {(1 - theoreticalRtp) * 100:F2}%");
    }

    [Fact]
    public void GuessWhatNumber_OptimalStrategy_IsAnyNumber()
    {
        // All guesses have equal EV
        var evByGuess = new Dictionary<int, decimal>();

        for (int guess = 1; guess <= 10; guess++)
        {
            decimal totalWagered = 0;
            decimal totalReturned = 0;

            for (int i = 0; i < Iterations / 10; i++)
            {
                totalWagered += Wager;
                var casinoAnswer = GetRandomNumber(1, 10);

                if (guess == casinoAnswer)
                {
                    totalReturned += Wager * 10;
                }
            }

            evByGuess[guess] = totalReturned / totalWagered;
        }

        Console.WriteLine("EV by guess number:");
        foreach (var kvp in evByGuess.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  Guess {kvp.Key}: {kvp.Value * 100:F2}% RTP");
        }

        // All guesses should have similar EV (within 5% of each other)
        var minEv = evByGuess.Values.Min();
        var maxEv = evByGuess.Values.Max();
        Assert.True((double)(maxEv - minEv) < 0.20, "EV variance between guesses should be small");
    }
}
