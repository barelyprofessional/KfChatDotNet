using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KfChatDotNetBot.Commands;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using NLog;

namespace KfChatDotNetBot.Services;

public static class RateLimitService
{
    private static Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Check whether a user is rate limited for a given command
    /// </summary>
    /// <param name="user">User you wish to check</param>
    /// <param name="command">Command the user is invoking</param>
    /// <param name="message">Message the user sent</param>
    /// <returns></returns>
    public static IsRateLimitedModel IsRateLimited(UserDbModel user, ICommand command, string message)
    {
        var result = new IsRateLimitedModel
        {
            IsRateLimited = false
        };
        if (command.RateLimitOptions == null) return result;
        if (command.RateLimitOptions.Flags.HasFlag(RateLimitFlags.ExemptPrivilegedUsers) &&
            user.UserRight > UserRight.Guest) return result;
        var entries = GetBucketEntries(command.GetType().Name);
        if (!command.RateLimitOptions.Flags.HasFlag(RateLimitFlags.Global))
        {
            entries = entries.Where(x => x.UserId == user.Id).ToList();
        }

        if (command.RateLimitOptions.Flags.HasFlag(RateLimitFlags.UseEntireMessage))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)));
            entries = entries.Where(x => x.MessageHash == hash).ToList();
        }

        var now = DateTimeOffset.UtcNow;
        entries = entries.Where(x => x.EntryExpires > now).ToList();
        if (entries.Count >= command.RateLimitOptions.MaxInvocations)
        {
            result.IsRateLimited = true;
            result.OldestEntryExpires = entries.OrderBy(x => x.EntryCreated).Last().EntryExpires;
        }
        return result;
    }

    /// <summary>
    /// Get all the bucket entries for a given command
    /// </summary>
    /// <param name="commandName">String representation of the command.
    /// Get it by running command.GetType().Name</param>
    /// <returns>A list of entries</returns>
    /// <exception cref="InvalidOperationException">Thrown if the cached entries were somehow null when converted to a string</exception>
    public static List<RateLimitBucketEntryModel> GetBucketEntries(string commandName)
    {
        var cache = MemoryCache.Default;
        var entries = cache.Get($"RateLimitBucket:{commandName}");
        if (entries == null) return [];
        List<RateLimitBucketEntryModel> bucketEntries;
        try
        {
            bucketEntries = JsonSerializer.Deserialize<List<RateLimitBucketEntryModel>>((string)entries) ??
                            throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            _logger.Error($"Caught an exception when trying to deserialize RateLimitBucket entries for {commandName}. JSON follows");
            _logger.Error(entries);
            _logger.Error("Exception follows");
            _logger.Error(e);
            return [];
        }

        return bucketEntries;
    }

    /// <summary>
    /// Save the current state of bucket entries for a given command
    /// </summary>
    /// <param name="commandName">String representation of the command.
    /// Get it by running command.GetType().Name</param>
    /// <param name="entries">Entries you wish to save</param>
    public static void SaveBucketEntries(string commandName, List<RateLimitBucketEntryModel> entries)
    {
        var cache = MemoryCache.Default;
        cache.Set($"RateLimitBucket:{commandName}", JsonSerializer.Serialize(entries),
            new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(1) });
    }

    /// <summary>
    /// Remove the most recent entry for a given user and command
    /// Use this if you want to invalidate an entry as forgiveness for invalid user input
    /// </summary>
    /// <param name="user">User to remove the entry for</param>
    /// <param name="command">Command the user ran</param>
    public static void RemoveMostRecentEntry(UserDbModel user, ICommand command)
    {
        var entries = GetBucketEntries(command.GetType().Name);
        var lastEntry = entries.Where(x => x.UserId == user.Id).OrderBy(x => x.EntryCreated).LastOrDefault();
        if (lastEntry == null) return;
        entries.Remove(lastEntry);
        SaveBucketEntries(command.GetType().Name, entries);
    }

    /// <summary>
    /// Add an entry to the rate limit bucket for the given command
    /// </summary>
    /// <param name="user">User the entry belongs to</param>
    /// <param name="command">Command the user ran</param>
    /// <param name="message">The user's message</param>
    public static void AddEntry(UserDbModel user, ICommand command, string message)
    {
        if (command.RateLimitOptions == null) return;
        var commandName = command.GetType().Name;
        var entries = GetBucketEntries(commandName);
        entries.Add(new RateLimitBucketEntryModel
        {
            UserId = user.Id,
            EntryCreated = DateTimeOffset.UtcNow,
            EntryExpires = DateTimeOffset.UtcNow + command.RateLimitOptions.Window,
            CommandInvoked = commandName,
            MessageHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)))
        });
        SaveBucketEntries(commandName, entries);
    }

    /// <summary>
    /// Removes entries which have expired for all commands in the rate limit bucket
    /// </summary>
    public static void CleanupExpiredEntries()
    {
        var cache = MemoryCache.Default;
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in cache.Select(kvp => kvp.Key).Where(kvp => kvp.StartsWith("RateLimitBucket:")).ToList().OfType<string>())
        {
            _logger.Info($"Cleaning up expired entries for {entry}");
            var commandName = entry.Replace("RateLimitBucket:", string.Empty);
            var entries = GetBucketEntries(commandName);
            SaveBucketEntries(commandName, entries.Where(x => x.EntryExpires > now).ToList());
        }
    }
}