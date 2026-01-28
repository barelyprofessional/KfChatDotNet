using KfChatDotNetBot.Models.DbModels;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot;

public class ApplicationDbContext : DbContext
{
    /// <summary>
    /// When set, this connection string will be used instead of the default db.sqlite.
    /// Useful for integration tests that want to use an in-memory or separate test database.
    /// </summary>
    public static string? TestConnectionString { get; set; }

    /// <summary>
    /// Default constructor using standard or test configuration
    /// </summary>
    public ApplicationDbContext()
    {
    }

    /// <summary>
    /// Constructor for dependency injection with options
    /// </summary>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        if (builder.IsConfigured)
            return;

        var connectionString = TestConnectionString ?? "Data Source=db.sqlite";
        builder.UseSqlite(connectionString);
    }
    
    public DbSet<UserDbModel> Users { get; set; }
    public DbSet<JuicerDbModel> Juicers { get; set; }
    public DbSet<SettingDbModel> Settings { get; set; }
    public DbSet<HowlggBetsDbModel> HowlggBets { get; set; }
    public DbSet<RainbetBetsDbModel> RainbetBets { get; set; }
    public DbSet<TwitchViewCountDbModel> TwitchViewCounts { get; set; }
    public DbSet<ChipsggBetDbModel> ChipsggBets { get; set; }
    public DbSet<ImageDbModel> Images { get; set; }
    public DbSet<UserWhoWasDbModel> UsersWhoWere { get; set; }
    // public DbSet<PocketWatchAddressDbModel> PocketWatchAddresses { get; set; }
    // public DbSet<PocketWatchTransactionDbModel> PocketWatchTransactions { get; set; }
    public DbSet<MomDbModel> Moms { get; set; }
    public DbSet<StreamDbModel> Streams { get; set; }
    public DbSet<GamblerDbModel> Gamblers { get; set; }
    public DbSet<TransactionDbModel> Transactions { get; set; }
    public DbSet<WagerDbModel> Wagers { get; set; }
    public DbSet<GamblerExclusionDbModel> Exclusions { get; set; }
    public DbSet<GamblerPerkDbModel> Perks { get; set; }
}