using System.Text;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot
{
    public class Program
    {
        static void Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("Opening up DB to perform a migration (if one is needed)");
            using var db = new ApplicationDbContext();
            db.Database.Migrate();
            logger.Info("Migration done. Syncing builtin settings keys");
            BuiltIn.SyncSettingsWithDb().Wait();
            logger.Info("Migrating settings from config.json (if needed)");
            BuiltIn.MigrateJsonSettingsToDb().Wait();
            logger.Info("Handing over to bot now");
            Console.OutputEncoding = Encoding.UTF8;
            new ChatBot();
        }
    }
}