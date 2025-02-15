using System.Text;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("Opening up DB to perform a migration (if one is needed)");
            await using var db = new ApplicationDbContext();
            await db.Database.MigrateAsync();
            logger.Info("Migration done. Syncing builtin settings keys");
            await BuiltIn.SyncSettingsWithDb();
            logger.Info("Migrating settings from config.json (if needed)");
            await BuiltIn.MigrateJsonSettingsToDb();
            logger.Info("Migrating images over to the new system");
            await BuiltIn.MigrateImages();
            logger.Info("Handing over to bot now");
            Console.OutputEncoding = Encoding.UTF8;
            new ChatBot();
        }
    }
}