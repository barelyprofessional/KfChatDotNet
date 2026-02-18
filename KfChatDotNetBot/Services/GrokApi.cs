using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// Grok AI (xAI) API integration for chat completions.
///
/// This service integrates with xAI's Grok models to provide AI-powered responses
/// for the !nora command. Grok uses an OpenAI-compatible API format.
///
/// API Documentation: https://docs.x.ai/api
/// API Endpoint: https://api.x.ai/v1/chat/completions
/// Pricing: ~$5 per 1M input tokens for grok-4-1-fast-reasoning
/// Console: https://console.x.ai/
///
/// Features:
/// - OpenAI-compatible chat completion format
/// - Configurable model (grok-4-1-fast-reasoning, grok-2-latest, etc.)
/// - Customizable system prompt for personality
/// - Response length limited to 200 tokens for chat brevity
///
/// Configuration:
/// - Grok.ApiKey: Your xAI API key (required)
/// - Grok.Chat.Endpoint: API endpoint (optional override)
/// - Grok.Nora.Model: Model to use (default: grok-4-1-fast-reasoning)
/// - Grok.Nora.SystemPrompt: Personality/instructions for Nora
/// - Proxy: Global proxy setting (optional)
/// </summary>
public static class GrokApi
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Chat completion response from Grok API.
    /// Follows OpenAI-compatible format.
    /// </summary>
    class ChatCompletionResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("object")] public string Object { get; set; } = string.Empty;
        [JsonPropertyName("created")] public long Created { get; set; }
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; } = new();
        [JsonPropertyName("usage")] public Usage Usage { get; set; } = new();
    }

    class ChatChoice
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("message")] public ChatMessage Message { get; set; } = new();
        [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = string.Empty;
    }

    class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }

    /// <summary>
    /// Sends a chat completion request to Grok AI.
    ///
    /// Flow:
    /// 1. Fetch API key and settings from database
    /// 2. Configure HTTP client with optional proxy
    /// 3. Build chat completion payload with system + user messages
    /// 4. Send POST request to Grok API
    /// 5. Parse response and extract message content
    ///
    /// Request parameters:
    /// - model: Configurable via settings or parameter (default: grok-4-1-fast-reasoning)
    /// - messages: System prompt + user message
    /// - temperature: 0.7 (balanced creativity)
    /// - max_tokens: 200 (keeps responses brief for chat)
    ///
    /// Error handling:
    /// - Returns null if API key is not configured
    /// - Returns null if HTTP request fails
    /// - Returns null if response is invalid
    /// - All errors are logged via NLog
    ///
    /// Cost considerations:
    /// - Each call costs based on input + output tokens
    /// - Typical cost: ~$0.0003 per interaction
    /// - Rate limiting (3/min/user) prevents runaway costs
    /// </summary>
    /// <param name="systemPrompt">Instructions for the AI (personality, constraints, etc.)</param>
    /// <param name="userMessage">The user's question/message</param>
    /// <param name="model">Optional model override (uses Grok.Nora.Model from settings if null)</param>
    /// <param name="maxTokens">Maximum response tokens (default 300)</param>
    /// <returns>The AI's response content, or null on error</returns>
    public static Task<string?> GetChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, int maxTokens = 300)
    {
        var messages = new List<ConversationMessage>
        {
            new() { Role = "user", Content = userMessage }
        };
        return GetChatCompletionAsync(systemPrompt, messages, model, maxTokens);
    }

    /// <summary>
    /// Sends a chat completion request to Grok AI with a full conversation history.
    /// </summary>
    /// <param name="systemPrompt">Instructions for the AI (personality, constraints, etc.)</param>
    /// <param name="messages">Conversation messages (system context summaries, user messages, assistant responses)</param>
    /// <param name="model">Optional model override (uses Grok.Nora.Model from settings if null)</param>
    /// <param name="maxTokens">Maximum response tokens (default 300)</param>
    /// <returns>The AI's response content, or null on error</returns>
    public static async Task<string?> GetChatCompletionAsync(string systemPrompt, List<ConversationMessage> messages, string? model = null, int maxTokens = 300)
    {
        Logger.Info("Sending chat completion request to Grok");

        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.GrokApiKey,
            BuiltIn.Keys.GrokChatEndpoint,
            BuiltIn.Keys.GrokNoraModel,
            BuiltIn.Keys.Proxy
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.GrokApiKey].Value))
        {
            Logger.Error("Grok API key is not set");
            return null;
        }

        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (settings[BuiltIn.Keys.Proxy].Value != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(settings[BuiltIn.Keys.Proxy].Value);
            Logger.Debug($"Using proxy {settings[BuiltIn.Keys.Proxy].Value}");
        }

        using var client = new HttpClient(handler);

        try
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings[BuiltIn.Keys.GrokApiKey].Value);

            var modelToUse = model ?? settings[BuiltIn.Keys.GrokNoraModel].Value ?? "grok-4-1-fast-reasoning";

            // Build the full message list: system prompt first, then conversation history
            var apiMessages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };
            apiMessages.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));

            var payload = new
            {
                model = modelToUse,
                messages = apiMessages,
                temperature = 0.7,
                max_tokens = maxTokens
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var endpoint = settings[BuiltIn.Keys.GrokChatEndpoint].Value
                ?? "https://api.x.ai/v1/chat/completions";
            var response = await client.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var completionResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody);

            if (completionResponse?.Choices == null || completionResponse.Choices.Count == 0)
            {
                Logger.Error("No completion returned from Grok API");
                return null;
            }

            return completionResponse.Choices[0].Message.Content;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while communicating with Grok API");
        }

        return null;
    }
}
