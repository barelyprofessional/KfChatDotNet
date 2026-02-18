using System.Collections.Concurrent;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ConversationContext
{
    public List<ConversationMessage> Messages { get; set; } = [];
    public string? Summary { get; set; }
    public int EstimatedTokenCount { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public string? Mood { get; set; }

    public void RecalculateTokens()
    {
        var tokens = 0;
        if (Summary != null)
            tokens += Summary.Length / 4;
        foreach (var msg in Messages)
            tokens += msg.Content.Length / 4 + 4; // +4 for role/message overhead
        EstimatedTokenCount = tokens;
    }
}

public static class ConversationContextManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<string, ConversationContext> Contexts = new();
    private static Task? _cleanupTask;
    private static CancellationToken _cancellationToken;

    public static void StartCleanupTimer(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _cleanupTask = CleanupLoop();
    }

    public static string GetContextKey(string mode, int userId, int roomId)
    {
        return mode.ToLowerInvariant() switch
        {
            "perchatter" => $"user:{userId}",
            "perroom" => $"room:{roomId}",
            _ => $"user:{userId}" // fallback to per-chatter
        };
    }

    public static string GetOrAssignMood(string contextKey)
    {
        var context = Contexts.GetOrAdd(contextKey, _ => new ConversationContext());
        if (context.Mood == null)
        {
            context.Mood = Commands.NoraMoods.GetRandomMood();
            Logger.Debug($"Assigned mood for {contextKey}: {context.Mood}");
        }
        return context.Mood;
    }

    public static void AddMessage(string contextKey, string role, string content)
    {
        var context = Contexts.GetOrAdd(contextKey, _ => new ConversationContext());
        context.Messages.Add(new ConversationMessage { Role = role, Content = content });
        context.LastActivity = DateTime.UtcNow;
        context.RecalculateTokens();
    }

    public static List<ConversationMessage> GetMessagesForApi(string contextKey)
    {
        if (!Contexts.TryGetValue(contextKey, out var context))
            return [];

        var messages = new List<ConversationMessage>();

        if (context.Summary != null)
        {
            messages.Add(new ConversationMessage
            {
                Role = "system",
                Content = $"Previous conversation summary: {context.Summary}"
            });
        }

        messages.AddRange(context.Messages);
        return messages;
    }

    public static async Task CompactIfNeededAsync(string contextKey)
    {
        if (!Contexts.TryGetValue(contextKey, out var context))
            return;

        var maxTokensSetting = await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextMaxTokens);
        var maxTokens = int.TryParse(maxTokensSetting.Value, out var mt) ? mt : 800;

        if (context.EstimatedTokenCount <= maxTokens)
            return;

        // Need at least 3 messages to compact (keep last 2, summarize the rest)
        if (context.Messages.Count < 3)
            return;

        Logger.Info($"Compacting context for {contextKey}: {context.EstimatedTokenCount} tokens > {maxTokens} limit");

        // Keep the last 2 messages, summarize everything else
        var keepCount = 2;
        var toSummarize = context.Messages.Take(context.Messages.Count - keepCount).ToList();
        var toKeep = context.Messages.Skip(context.Messages.Count - keepCount).ToList();

        // Build the text to summarize
        var summaryInput = "";
        if (context.Summary != null)
            summaryInput = $"Previous summary: {context.Summary}\n\n";

        summaryInput += string.Join("\n",
            toSummarize.Select(m => $"{m.Role}: {m.Content}"));

        var summary = await GrokApi.GetChatCompletionAsync(
            "Summarize this conversation in 2-3 concise sentences. Capture the key topics and any important details the user mentioned.",
            summaryInput,
            maxTokens: 150);

        if (summary != null)
        {
            context.Summary = summary;
            context.Messages = toKeep;
            context.RecalculateTokens();
            Logger.Info($"Compacted context for {contextKey}: now {context.EstimatedTokenCount} tokens");
        }
        else
        {
            // Compaction failed — just drop the oldest messages to stay under budget
            Logger.Warn($"Compaction API call failed for {contextKey}, dropping oldest messages instead");
            context.Messages = toKeep;
            context.RecalculateTokens();
        }
    }

    public static bool ClearContext(string contextKey)
    {
        return Contexts.TryRemove(contextKey, out _);
    }

    private static async Task CleanupLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(_cancellationToken))
        {
            try
            {
                await CleanupExpired();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during conversation context cleanup");
            }
        }
    }

    private static async Task CleanupExpired()
    {
        var expirySetting = await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextExpiryMinutes);
        var expiryMinutes = int.TryParse(expirySetting.Value, out var em) ? em : 30;
        var cutoff = DateTime.UtcNow.AddMinutes(-expiryMinutes);

        var expired = Contexts.Where(kvp => kvp.Value.LastActivity < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
        {
            Contexts.TryRemove(key, out _);
            Logger.Debug($"Expired conversation context: {key}");
        }

        if (expired.Count > 0)
            Logger.Info($"Cleaned up {expired.Count} expired conversation contexts");
    }
}
