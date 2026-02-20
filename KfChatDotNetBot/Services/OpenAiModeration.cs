using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// OpenAI Moderation API integration for content filtering.
///
/// This service uses OpenAI's free Moderation API to detect potentially harmful content.
/// The moderation categories are used to filter out illegal content while allowing
/// offensive but legal content (profanity, hate speech, etc.).
///
/// API Documentation: https://platform.openai.com/docs/api-reference/moderations
/// API Endpoint: https://api.openai.com/v1/moderations
/// Cost: Free (but has rate limits)
///
/// Content Policy:
/// - BLOCK: illicit activities, self-harm instructions, CSAM
/// - ALLOW: profanity, harassment, hate speech, adult sexual content, violence
///
/// Configuration:
/// - OpenAi.ApiKey: Your OpenAI API key (required)
/// - OpenAi.Moderation.Endpoint: API endpoint (optional override)
/// - Proxy: Global proxy setting (optional)
/// </summary>
public static class OpenAiModeration
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Response wrapper from OpenAI Moderation API.
    /// Contains model info and list of moderation results.
    /// </summary>
    public class ModerationResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("results")] public List<ModerationResult> Results { get; set; } = new();
    }

    public class ModerationResult
    {
        [JsonPropertyName("flagged")] public bool Flagged { get; set; }
        [JsonPropertyName("categories")] public ModerationCategories Categories { get; set; } = new();
        [JsonPropertyName("category_scores")] public Dictionary<string, double> CategoryScores { get; set; } = new();
    }

    public class ModerationCategories
    {
        [JsonPropertyName("harassment")] public bool Harassment { get; set; }
        [JsonPropertyName("harassment/threatening")] public bool HarassmentThreatening { get; set; }
        [JsonPropertyName("sexual")] public bool Sexual { get; set; }
        [JsonPropertyName("hate")] public bool Hate { get; set; }
        [JsonPropertyName("hate/threatening")] public bool HateThreatening { get; set; }
        [JsonPropertyName("illicit")] public bool Illicit { get; set; }
        [JsonPropertyName("illicit/violent")] public bool IllicitViolent { get; set; }
        [JsonPropertyName("self-harm")] public bool SelfHarm { get; set; }
        [JsonPropertyName("self-harm/intent")] public bool SelfHarmIntent { get; set; }
        [JsonPropertyName("self-harm/instructions")] public bool SelfHarmInstructions { get; set; }
        [JsonPropertyName("sexual/minors")] public bool SexualMinors { get; set; }
        [JsonPropertyName("violence")] public bool Violence { get; set; }
        [JsonPropertyName("violence/graphic")] public bool ViolenceGraphic { get; set; }
    }

    /// <summary>
    /// Determines if content is "illegal" (vs just profane/offensive).
    ///
    /// This method defines the content policy for the !nora command by deciding
    /// what gets blocked vs what gets allowed through to the AI.
    ///
    /// BLOCKED categories (return true):
    /// - illicit: Instructions for illegal activities (bomb-making, drug manufacturing, hacking)
    /// - illicit/violent: Violent illegal activities
    /// - self-harm/instructions: Detailed methods for self-harm
    /// - sexual/minors: Any content involving minors (CSAM)
    ///
    /// ALLOWED categories (return false):
    /// - harassment: Insults, bullying, threatening language
    /// - hate: Hate speech, slurs
    /// - sexual: Adult sexual content
    /// - violence: Descriptions of violence
    /// - violence/graphic: Graphic violence
    ///
    /// Design rationale:
    /// The bot operates in an edgy chat environment where profanity and offensive
    /// language are common. This policy allows that culture while still preventing
    /// the bot from being used to generate truly dangerous or illegal content.
    /// </summary>
    /// <param name="categories">The moderation categories from OpenAI</param>
    /// <returns>True if content should be blocked, false if it should be allowed</returns>
    public static bool IsIllegalContent(ModerationCategories categories)
    {
        return categories.Illicit ||
               categories.IllicitViolent ||
               categories.SelfHarmInstructions ||
               categories.SexualMinors;
    }

    /// <summary>
    /// Sends content to OpenAI Moderation API for analysis.
    ///
    /// Flow:
    /// 1. Fetch API key and settings from database
    /// 2. Configure HTTP client with optional proxy
    /// 3. Send POST request to OpenAI with input text
    /// 4. Parse response and return first moderation result
    ///
    /// Error handling:
    /// - Returns null if API key is not configured
    /// - Returns null if HTTP request fails
    /// - Returns null if response is invalid
    /// - All errors are logged via NLog
    ///
    /// The calling code should treat null as a failure and block the content
    /// as a safety precaution (fail-safe behavior).
    /// </summary>
    /// <param name="input">The text to moderate</param>
    /// <returns>ModerationResult with flagged categories, or null on error</returns>
    public static async Task<ModerationResult?> ModerateContentAsync(string input)
    {
        Logger.Info("Sending moderation request to OpenAI");

        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.OpenAiApiKey,
            BuiltIn.Keys.OpenAiModerationEndpoint,
            BuiltIn.Keys.Proxy
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.OpenAiApiKey].Value))
        {
            Logger.Error("OpenAI API key is not set");
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
                new AuthenticationHeaderValue("Bearer", settings[BuiltIn.Keys.OpenAiApiKey].Value);

            var payload = new { input };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var endpoint = settings[BuiltIn.Keys.OpenAiModerationEndpoint].Value
                ?? "https://api.openai.com/v1/moderations";
            var response = await client.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var moderationResponse = JsonSerializer.Deserialize<ModerationResponse>(responseBody);

            if (moderationResponse?.Results == null || moderationResponse.Results.Count == 0)
            {
                Logger.Error("No moderation results returned from OpenAI");
                return null;
            }

            return moderationResponse.Results[0];
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while communicating with OpenAI Moderation API");
        }

        return null;
    }
}
