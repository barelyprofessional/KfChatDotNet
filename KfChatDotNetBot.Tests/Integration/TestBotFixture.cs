using KfChatDotNetBot;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Tests.Integration;

/// <summary>
/// Fixture that sets up a test bot instance with an isolated SQLite database.
/// Use this for integration tests that need to invoke real commands and measure results.
/// </summary>
public class TestBotFixture : IDisposable
{
    public ChatBot Bot { get; private set; }
    public string DatabasePath { get; private set; }
    public int TestUserId { get; private set; }
    public string TestUsername { get; private set; }
    public int TestUserKfId { get; private set; }

    private readonly CancellationTokenSource _cts = new();

    public TestBotFixture()
    {
        // Create a unique database file for this test run
        DatabasePath = Path.Combine(Path.GetTempPath(), $"kfbot_test_{Guid.NewGuid():N}.sqlite");
        var connectionString = $"Data Source={DatabasePath}";

        // Configure the database to use our test file
        ApplicationDbContext.TestConnectionString = connectionString;

        // Initialize the database
        InitializeDatabase();

        // Create the test bot in test mode
        Bot = new ChatBot(testMode: true, cancellationToken: _cts.Token);

        TestUsername = "TestGambler";
        TestUserKfId = 999999;
    }

    private void InitializeDatabase()
    {
        using var db = new ApplicationDbContext();

        // Create the schema
        db.Database.Migrate();

        // Sync built-in settings (this sets up Money.Enabled etc.)
        BuiltIn.SyncSettingsWithDb().Wait();

        // Enable the casino
        SettingsProvider.SetValueAsBooleanAsync(BuiltIn.Keys.MoneyEnabled, true).Wait();

        // Create a test user with high balance
        var testUser = new UserDbModel
        {
            KfId = 999999,
            KfUsername = "TestGambler",
            UserRight = UserRight.TrueAndHonest,
            Ignored = false
        };
        db.Users.Add(testUser);
        db.SaveChanges();

        TestUserId = testUser.Id;

        // Create gambler account with massive balance
        var gambler = new GamblerDbModel
        {
            User = testUser,
            Balance = 1_000_000_000_000m, // 1 trillion
            TotalWagered = 0,
            RandomSeed = Guid.NewGuid().ToString(),
            NextVipLevelWagerRequirement = Money.VipLevels[0].BaseWagerRequirement,
            State = GamblerState.Active,
            Created = DateTimeOffset.UtcNow
        };
        db.Gamblers.Add(gambler);
        db.SaveChanges();
    }

    /// <summary>
    /// Send a command as the test user and wait for it to complete
    /// </summary>
    public void SendCommand(string command)
    {
        Bot.ProcessTestMessage(command, TestUserKfId, TestUsername);
        // Give async command handlers time to complete
        Thread.Sleep(50);
    }

    /// <summary>
    /// Send multiple commands rapidly
    /// </summary>
    public void SendCommands(IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            Bot.ProcessTestMessage(command, TestUserKfId, TestUsername);
        }
        // Wait for all commands to process
        Thread.Sleep(100);
    }

    /// <summary>
    /// Get all wagers from the test database
    /// </summary>
    public List<WagerDbModel> GetAllWagers()
    {
        using var db = new ApplicationDbContext();
        return db.Wagers.Include(w => w.Gambler).ToList();
    }

    /// <summary>
    /// Get wagers for a specific game type
    /// </summary>
    public List<WagerDbModel> GetWagersByGame(WagerGame game)
    {
        using var db = new ApplicationDbContext();
        return db.Wagers
            .Include(w => w.Gambler)
            .Where(w => w.Game == game)
            .ToList();
    }

    /// <summary>
    /// Calculate RTP from wagers in the database
    /// </summary>
    public (decimal totalWagered, decimal totalReturned, double rtpPercent) CalculateRtp(WagerGame? game = null)
    {
        using var db = new ApplicationDbContext();
        var query = db.Wagers.AsQueryable();

        if (game != null)
            query = query.Where(w => w.Game == game);

        var wagers = query.ToList();

        if (wagers.Count == 0)
            return (0, 0, 0);

        var totalWagered = wagers.Sum(w => w.WagerAmount);
        var totalReturned = wagers.Sum(w => w.WagerEffect + w.WagerAmount);

        var rtpPercent = totalWagered == 0 ? 0 : (double)totalReturned / (double)totalWagered * 100;

        return (totalWagered, totalReturned, rtpPercent);
    }

    /// <summary>
    /// Get the test user's current balance
    /// </summary>
    public decimal GetBalance()
    {
        using var db = new ApplicationDbContext();
        var gambler = db.Gamblers.FirstOrDefault(g => g.User.KfId == TestUserKfId);
        return gambler?.Balance ?? 0;
    }

    /// <summary>
    /// Reset the test user's balance and clear wagers
    /// </summary>
    public void ResetForNewTest()
    {
        using var db = new ApplicationDbContext();

        // Clear all wagers
        db.Wagers.RemoveRange(db.Wagers);

        // Reset gambler balance
        var gambler = db.Gamblers.Include(g => g.User).FirstOrDefault(g => g.User.KfId == TestUserKfId);
        if (gambler != null)
        {
            gambler.Balance = 1_000_000_000_000m;
            gambler.TotalWagered = 0;
        }

        db.SaveChanges();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        // Clean up the test connection string
        ApplicationDbContext.TestConnectionString = null;

        // Delete the test database file
        if (File.Exists(DatabasePath))
        {
            try
            {
                File.Delete(DatabasePath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Collection definition for sharing a single bot fixture across multiple test classes
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<TestBotFixture>
{
}
