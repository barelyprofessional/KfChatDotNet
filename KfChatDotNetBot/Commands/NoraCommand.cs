using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Commands;

/// <summary>
/// The !nora command allows users to interact with Grok AI (xAI) through the chat.
/// All messages are moderated via OpenAI's Moderation API to filter illegal content
/// while allowing profanity and general offensive language.
///
/// Supports per-chatter or per-room conversation context with automatic compaction.
/// Use "!nora reset" to clear your conversation history.
///
/// Flow:
/// 1. Validate input (15 words max, 140 chars max)
/// 2. Moderate content via OpenAI (blocks illegal, allows profanity)
/// 3. Build conversation context (if enabled)
/// 4. Send to Grok AI for response
/// 5. Store exchange in context and compact if needed
/// 6. Post formatted response to chat
///
/// Configuration required:
/// - OpenAi.ApiKey: OpenAI API key for moderation (free)
/// - Grok.ApiKey: xAI API key for Grok (~$0.20 per 1M input tokens)
/// - Grok.Nora.ContextMode: perChatter, perRoom, or disabled
///
/// See NORA_SETUP.md for detailed setup instructions.
/// </summary>
public class NoraCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly string? PromptFilePath = FindPromptFile();
    private static string? _cachedPrompt;
    private static DateTime _cachedPromptLastModified;

    private static string? FindPromptFile()
    {
        // Check next to the executable first, then fall back to the Commands directory in the source tree
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, "Commands", "NoraPrompt.txt");
        if (File.Exists(candidate)) return candidate;

        candidate = Path.Combine(exeDir, "NoraPrompt.txt");
        if (File.Exists(candidate)) return candidate;

        Logger.Error("NoraPrompt.txt not found in {exeDir}/Commands/ or {exeDir}/", exeDir);
        return null;
    }

    private static string? LoadPrompt()
    {
        if (PromptFilePath == null) return null;

        var lastWrite = File.GetLastWriteTimeUtc(PromptFilePath);
        if (_cachedPrompt != null && lastWrite == _cachedPromptLastModified)
            return _cachedPrompt;

        try
        {
            _cachedPrompt = File.ReadAllText(PromptFilePath).Trim();
            _cachedPromptLastModified = lastWrite;
            Logger.Info("Loaded Nora prompt from {path} ({length} chars)", PromptFilePath, _cachedPrompt.Length);
            return _cachedPrompt;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read NoraPrompt.txt");
            return null;
        }
    }

    public List<Regex> Patterns => [
        new Regex(@"^nora\s+(?<message>.+)", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "Ask Nora AI a question (max 15 words, 140 chars). Use '!nora reset' to clear context.";

    public UserRight RequiredRight => UserRight.Loser;

    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        Window = TimeSpan.FromMinutes(1),
        MaxInvocations = 3,
        Flags = RateLimitFlags.None
    };

    private const int MaxWords = 30;
    private const int MaxCharacters = 300;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user,
        GroupCollection arguments, CancellationToken ctx)
    {
        var userMessage = arguments["message"].Value.Trim();

        // Handle !nora reset — clear conversation context
        if (userMessage.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            var modeSetting = await SettingsProvider.GetValueAsync(BuiltIn.Keys.GrokNoraContextMode);
            var mode = modeSetting.Value ?? "perChatter";

            if (mode.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, conversation context is disabled.",
                    true,
                    autoDeleteAfter: TimeSpan.FromSeconds(15));
                return;
            }

            var resetKey = ConversationContextManager.GetContextKey(mode, user.KfId, message.RoomId);
            var cleared = ConversationContextManager.ClearContext(resetKey);
            await botInstance.SendChatMessageAsync(
                cleared
                    ? $"{user.FormatUsername()}, your conversation context has been cleared."
                    : $"{user.FormatUsername()}, you don't have an active conversation context.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        // Validate word count
        var wordCount = userMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > MaxWords)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your message has {wordCount} words. Maximum is {MaxWords} words.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        // Validate character count
        if (userMessage.Length > MaxCharacters)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your message has {userMessage.Length} characters. Maximum is {MaxCharacters} characters.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        // Step 1: Moderate the content
        var moderationResult = await OpenAiModeration.ModerateContentAsync(userMessage);

        if (moderationResult == null)
        {
            Logger.Warn($"Moderation API failed for user {user.KfUsername}, blocking message as safety precaution");
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, moderation service is currently unavailable. Please try again later.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        if (OpenAiModeration.IsIllegalContent(moderationResult.Categories))
        {
            Logger.Warn($"User {user.KfUsername} attempted to send illegal content via Nora command: {userMessage}");
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your message was blocked for containing illegal content.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        if (moderationResult.Flagged)
        {
            Logger.Info($"User {user.KfUsername} sent flagged but allowed content (profanity/offensive): {userMessage}");
        }

        // Step 2: Build conversation context and get Grok AI response
        var basePrompt = LoadPrompt();
        if (basePrompt == null)
        {
            Logger.Error("Nora prompt file is missing or unreadable");
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, Nora's prompt file is missing. Please check the server configuration.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.GrokNoraContextMode,
            BuiltIn.Keys.GrokNoraUserInfoEnabled
        ]);

        var systemPrompt = basePrompt;
        var contextMode = settings[BuiltIn.Keys.GrokNoraContextMode].Value ?? "perChatter";
        var contextDisabled = contextMode.Equals("disabled", StringComparison.OrdinalIgnoreCase);

        // Compute context key once (used for mood and later for context messages)
        string? contextKey = null;
        if (!contextDisabled)
            contextKey = ConversationContextManager.GetContextKey(contextMode, user.KfId, message.RoomId);

        // Optionally inject user info into the system prompt
        var userInfoEnabled = settings[BuiltIn.Keys.GrokNoraUserInfoEnabled].Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        if (userInfoEnabled)
        {
            var infoParts = new List<string>
            {
                $"Username: {user.KfUsername}",
                $"Permission level: {user.UserRight}"
            };

            var gambler = await Money.GetGamblerEntityAsync(user.Id, createIfNoneExists: false, ct: ctx);
            if (gambler != null && gambler.State == GamblerState.Active)
            {
                infoParts.Add($"Kasino balance: {gambler.Balance:N2} KKK");
                infoParts.Add($"Total wagered: {gambler.TotalWagered:N2} KKK");
                var vipPerk = await Money.GetVipLevelAsync(gambler, ctx);
                if (vipPerk != null)
                    infoParts.Add($"VIP rank: {vipPerk.PerkName} (Tier {vipPerk.PerkTier})");
                else
                    infoParts.Add("VIP rank: None (hasn't reached first VIP level)");
            }
            else
            {
                infoParts.Add("Kasino status: Not an active gambler");
            }

            systemPrompt += "\n\nThe customer you are currently speaking to has the following profile:\n" + string.Join("\n", infoParts);
        }

        // Inject mood into system prompt
        var mood = contextKey != null
            ? ConversationContextManager.GetOrAssignMood(contextKey)
            : NoraMoods.GetRandomMood();
        systemPrompt += "\n\n" + mood;

        string? grokResponse;

        if (contextDisabled)
        {
            // Stateless mode — same as before
            grokResponse = await GrokApi.GetChatCompletionAsync(systemPrompt, userMessage);
        }
        else
        {
            // Context-aware mode

            // Get existing context messages and append current user message
            // In perRoom mode, prefix with username so the AI knows who said what
            var contentForContext = contextMode.Equals("perroom", StringComparison.OrdinalIgnoreCase)
                ? $"{user.KfUsername}: {userMessage}"
                : userMessage;

            var contextMessages = ConversationContextManager.GetMessagesForApi(contextKey!);
            contextMessages.Add(new ConversationMessage { Role = "user", Content = contentForContext });

            grokResponse = await GrokApi.GetChatCompletionAsync(systemPrompt, contextMessages);

            if (grokResponse != null)
            {
                // Store the exchange in context
                ConversationContextManager.AddMessage(contextKey!, "user", contentForContext);
                ConversationContextManager.AddMessage(contextKey!, "assistant", grokResponse);

                // Compact if context is getting too large
                await ConversationContextManager.CompactIfNeededAsync(contextKey!);
            }
        }

        if (grokResponse == null)
        {
            Logger.Error($"Grok API failed for user {user.KfUsername}");
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, Nora is currently unavailable. Please try again later.",
                true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        // Step 3: Send response to chat with user avatar
        // var avatarTag = "";
        // if (message.Author.AvatarUrl != null)
        // {
        //     var avatarPath = message.Author.AvatarUrl.IsAbsoluteUri
        //         ? message.Author.AvatarUrl.PathAndQuery
        //         : message.Author.AvatarUrl.OriginalString;
        //     avatarPath = avatarPath.Replace("/data/avatars/m/", "/data/avatars/s/");
        //     avatarTag = $"[img]https://uploads.kiwifarms.st{avatarPath}[/img] ";
        // }

        // var formattedResponse = $"{avatarTag}[b]Nora to {user.FormatUsername()}:[/b] {grokResponse}";
        var formattedResponse = $"[b]Nora to {user.FormatUsername()}:[/b] {grokResponse}";

        await botInstance.SendChatMessageAsync(
            formattedResponse,
            true,
            ChatBot.LengthLimitBehavior.TruncateNicely);
    }
}
