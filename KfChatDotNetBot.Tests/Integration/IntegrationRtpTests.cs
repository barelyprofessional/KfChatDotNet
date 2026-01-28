using KfChatDotNetBot;
using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Tests.Integration;

/// <summary>
/// Integration tests that invoke real commands through the bot and measure RTP
/// from actual wager records in the database.
///
/// These tests verify the full command pipeline:
/// - Command parsing and validation
/// - Gambler balance checks
/// - Random number generation
/// - Wager recording
/// - Balance updates
/// </summary>
[Collection("IntegrationTests")]
public class IntegrationRtpTests : IDisposable
{
    private readonly TestBotFixture _fixture;

    public IntegrationRtpTests(TestBotFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetForNewTest();
    }

    public void Dispose()
    {
        // Cleanup handled by fixture
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Dice_ThousandWagers_RtpIsReasonable()
    {
        const int iterations = 1000;
        const int wagerAmount = 100;

        // Send 1000 dice commands
        for (int i = 0; i < iterations; i++)
        {
            _fixture.SendCommand($"!dice {wagerAmount}");
        }

        // Wait for all commands to complete
        Thread.Sleep(500);

        // Calculate RTP from database
        var (totalWagered, totalReturned, rtp) = _fixture.CalculateRtp(WagerGame.Dice);

        Console.WriteLine($"Dice Integration RTP: {rtp:F2}% over {iterations} wagers");
        Console.WriteLine($"Total Wagered: {totalWagered:N2}, Total Returned: {totalReturned:N2}");

        var wagers = _fixture.GetWagersByGame(WagerGame.Dice);
        Console.WriteLine($"Wager count in DB: {wagers.Count}");

        // Dice has ~1.5% house edge, so RTP should be around 97%
        // Allow wide range due to variance with only 1000 iterations
        Assert.True(wagers.Count > 0, "No wagers were recorded in the database");
        Assert.InRange(rtp, 80.0, 115.0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Limbo_ThousandWagers_RtpIsReasonable()
    {
        const int iterations = 1000;
        const int wagerAmount = 100;
        const string multiplier = "2"; // 2x multiplier

        // Send 1000 limbo commands
        for (int i = 0; i < iterations; i++)
        {
            _fixture.SendCommand($"!limbo {wagerAmount} {multiplier}");
        }

        // Wait for all commands to complete
        Thread.Sleep(500);

        // Calculate RTP from database
        var (totalWagered, totalReturned, rtp) = _fixture.CalculateRtp(WagerGame.Limbo);

        Console.WriteLine($"Limbo Integration RTP: {rtp:F2}% over {iterations} wagers");
        Console.WriteLine($"Total Wagered: {totalWagered:N2}, Total Returned: {totalReturned:N2}");

        var wagers = _fixture.GetWagersByGame(WagerGame.Limbo);
        Console.WriteLine($"Wager count in DB: {wagers.Count}");

        // Limbo at 2x should have ~50% win rate with ~100% RTP (fair odds)
        Assert.True(wagers.Count > 0, "No wagers were recorded in the database");
        Assert.InRange(rtp, 80.0, 120.0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MultipleGames_MixedWagers_AllRecordedCorrectly()
    {
        const int wagerAmount = 100;
        const int iterations = 100;

        // Mix of different games
        for (int i = 0; i < iterations; i++)
        {
            _fixture.SendCommand($"!dice {wagerAmount}");
            _fixture.SendCommand($"!limbo {wagerAmount} 2");
        }

        Thread.Sleep(500);

        var diceWagers = _fixture.GetWagersByGame(WagerGame.Dice);
        var limboWagers = _fixture.GetWagersByGame(WagerGame.Limbo);
        var allWagers = _fixture.GetAllWagers();

        Console.WriteLine($"Dice wagers: {diceWagers.Count}");
        Console.WriteLine($"Limbo wagers: {limboWagers.Count}");
        Console.WriteLine($"Total wagers: {allWagers.Count}");

        // Verify both game types were recorded
        Assert.True(diceWagers.Count > 0, "Dice wagers should be recorded");
        Assert.True(limboWagers.Count > 0, "Limbo wagers should be recorded");

        // Verify total matches sum
        Assert.Equal(allWagers.Count, diceWagers.Count + limboWagers.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void BalanceUpdates_AfterWagers_ReflectsWinLoss()
    {
        var initialBalance = _fixture.GetBalance();
        const int wagerAmount = 1000;
        const int iterations = 100;

        // Send wagers
        for (int i = 0; i < iterations; i++)
        {
            _fixture.SendCommand($"!dice {wagerAmount}");
        }

        Thread.Sleep(500);

        var finalBalance = _fixture.GetBalance();
        var (totalWagered, totalReturned, rtp) = _fixture.CalculateRtp(WagerGame.Dice);

        Console.WriteLine($"Initial Balance: {initialBalance:N2}");
        Console.WriteLine($"Final Balance: {finalBalance:N2}");
        Console.WriteLine($"Total Wagered: {totalWagered:N2}");
        Console.WriteLine($"Total Returned: {totalReturned:N2}");
        Console.WriteLine($"Expected Final: {initialBalance - totalWagered + totalReturned:N2}");

        // Balance change should match wager effects
        var expectedBalance = initialBalance - totalWagered + totalReturned;

        // Allow small tolerance for floating point
        Assert.InRange(finalBalance, expectedBalance - 1, expectedBalance + 1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void InsufficientBalance_WagerRejected_NoWagerRecorded()
    {
        // Reset with low balance
        _fixture.ResetForNewTest();

        // Set balance to very low
        using (var db = new ApplicationDbContext())
        {
            var gambler = db.Gamblers.First();
            gambler.Balance = 50m;
            db.SaveChanges();
        }

        // Try to wager more than balance
        _fixture.SendCommand("!dice 100");

        Thread.Sleep(100);

        var wagers = _fixture.GetAllWagers();

        // No wager should be recorded since balance was insufficient
        Assert.Empty(wagers);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Dice_LargeWagerCount_RtpConvergesToExpected()
    {
        const int iterations = 5000;
        const int wagerAmount = 100;

        // Send many dice commands
        for (int i = 0; i < iterations; i++)
        {
            _fixture.SendCommand($"!dice {wagerAmount}");

            // Small delay every 100 commands to let async handlers complete
            if (i % 100 == 0)
                Thread.Sleep(50);
        }

        Thread.Sleep(1000);

        var (totalWagered, totalReturned, rtp) = _fixture.CalculateRtp(WagerGame.Dice);
        var wagers = _fixture.GetWagersByGame(WagerGame.Dice);

        Console.WriteLine($"Dice Integration RTP: {rtp:F2}% over {wagers.Count} wagers");
        Console.WriteLine($"Expected RTP: ~97% (1.5% house edge)");

        // With 5000 iterations, RTP should be closer to expected
        // Dice has 1.5% house edge = 97% expected RTP
        Assert.True(wagers.Count >= iterations * 0.9,
            $"Expected at least {iterations * 0.9} wagers, got {wagers.Count}");

        // Tighter bounds with more samples
        Assert.InRange(rtp, 90.0, 105.0);
    }
}
