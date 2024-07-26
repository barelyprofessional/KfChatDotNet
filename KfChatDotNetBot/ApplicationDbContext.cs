using KfChatDotNetBot.Models.DbModels;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot;

public class ApplicationDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        builder.UseSqlite("Data Source=db.sqlite");
    }
    
    public DbSet<UserDbModel> Users { get; set; }
    public DbSet<JuicerDbModel> Juicers { get; set; }
    public DbSet<SettingDbModel> Settings { get; set; }
    public DbSet<HowlggBetsDbModel> HowlggBets { get; set; }
}