using Microsoft.Data.Sqlite;
using NLog;

namespace KfChatDotNetKickBot;

public static class Helpers
{
    // This ended up being pretty useless as it turns out Firefox doesn't store session cookies in cookies.sqlite
    // But I'll leave it here in case it becomes useful one day
    public static async Task<string?> GetXfToken(string cookieName, string cookieDomain, string containerPath)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var connection = new SqliteConnection($"Data Source={containerPath}");
        
        await connection.OpenAsync();
        logger.Debug($"Opened {containerPath}");

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM moz_cookies WHERE host = $host AND name = $name ORDER BY creationTime DESC LIMIT 1";
        command.Parameters.AddWithValue("$host", cookieDomain);
        command.Parameters.AddWithValue("$name", cookieName);
        logger.Debug("Created command");
        logger.Debug(command.CommandText);

        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            logger.Debug("Reading first row, which will be immediately returned anyway");
            return reader.GetString(0);
        }
        
        logger.Error("Fucked up while retrieving cookie. Cookie doesn't exist?");
        return null;
    }
}