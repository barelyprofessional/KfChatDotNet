using System.Text.Json;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;

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

public class ConversationContextManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private IDatabase _redisDb;

    public ConversationContextManager()
    {
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            Logger.Error($"Can't initialize the Nora ConversationContextManager service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            throw new InvalidOperationException("Redis isn't configured");
        }
        
        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
    }

    public static string GetContextKeyAsync(string mode, int userId, int roomId)
    {
        return mode.ToLowerInvariant() switch
        {
            "perchatter" => $"Nora:User:{userId}",
            "perroom" => $"Nora:Room:{roomId}",
            _ => $"Nora:User:{userId}" // fallback to per-chatter
        };
    }

    public async Task<string> GetOrAssignMoodAsync(string contextKey)
    {
        var data = await _redisDb.StringGetAsync(contextKey);
        var context = new ConversationContext();
        if (data.HasValue)
        {
            context = JsonSerializer.Deserialize<ConversationContext>(data.ToString());
        }

        if (context == null)
        {
            throw new InvalidOperationException($"Caught a null when deserializing {contextKey}");
        }
        if (context.Mood == null)
        {
            context.Mood = await GetRandomMoodAsync();
            Logger.Debug($"Assigned mood for {contextKey}: {context.Mood}");
            var expiration =
                TimeSpan.FromMinutes((await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextExpiryMinutes))
                    .ToType<int>());
            await _redisDb.StringSetAsync(contextKey, JsonSerializer.Serialize(context), expiration, When.Always);
        }

        return context.Mood;
    }

    public async Task AddMessageAsync(string contextKey, string role, string content)
    {
        var data = await _redisDb.StringGetAsync(contextKey);
        var context = new ConversationContext();
        if (data.HasValue)
        {
            context = JsonSerializer.Deserialize<ConversationContext>(data.ToString());
        }
        if (context == null)
        {
            throw new InvalidOperationException($"Caught a null when deserializing {contextKey}");
        }
        context.Messages.Add(new ConversationMessage { Role = role, Content = content });
        context.LastActivity = DateTime.UtcNow;
        context.RecalculateTokens();
        var expiration =
            TimeSpan.FromMinutes((await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextExpiryMinutes))
                .ToType<int>());
        await _redisDb.StringSetAsync(contextKey, JsonSerializer.Serialize(context), expiration, When.Always);
    }

    public async Task<List<ConversationMessage>> GetMessagesForApiAsync(string contextKey)
    {
        var data = await _redisDb.StringGetAsync(contextKey);
        if (data.IsNullOrEmpty)
        {
            return [];
        }

        var context = JsonSerializer.Deserialize<ConversationContext>(data.ToString());
        if (context == null)
        {
            throw new InvalidOperationException($"Caught a null when deserializing {contextKey}");
        }

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

    public async Task CompactIfNeededAsync(string contextKey)
    {
        var data = await _redisDb.StringGetAsync(contextKey);
        if (data.IsNullOrEmpty)
        {
            return;
        }

        var context = JsonSerializer.Deserialize<ConversationContext>(data.ToString());
        if (context == null)
        {
            throw new InvalidOperationException($"Caught a null when deserializing {contextKey}");
        }

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
        var expiration =
            TimeSpan.FromMinutes((await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextExpiryMinutes))
                .ToType<int>());
        await _redisDb.StringSetAsync(contextKey, JsonSerializer.Serialize(context), expiration, When.Always);
    }
    
    public async Task<bool> ClearContextAsync(string contextKey)
    {
        return await _redisDb.KeyDeleteAsync(contextKey);
    }
    
    public static async Task<string> GetRandomMoodAsync()
    {
        var moods = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraMoods)).JsonDeserialize<List<string>>();
        if (moods == null)
        {
            throw new InvalidOperationException("Caught a null when deserializing Nora's moods");
        }
        return moods[Random.Shared.Next(moods.Count)];
    }
}
