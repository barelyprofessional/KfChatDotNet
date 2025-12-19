using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public static class OpenRouter
{
    class OrResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("provider")] public string Provider { get; set; } = string.Empty;
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("object")] public string Object { get; set; } = string.Empty;
        [JsonPropertyName("created")] public long Created { get; set; }
        [JsonPropertyName("choices")] public List<OrChoice> Choices { get; set; } = new List<OrChoice>();
        [JsonPropertyName("usage")] public Usage Usage { get; set; } = new Usage();
    }

    class OrChoice
    {
        [JsonPropertyName("logprobs")] public object? Logprobs { get; set; }
        [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = string.Empty;
        [JsonPropertyName("native_finish_reason")] public string NativeFinishReason { get; set; } = string.Empty;
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("message")] public OrMessage Message { get; set; } = new OrMessage();
    }

    class OrMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        [JsonPropertyName("refusal")] public object? Refusal { get; set; }
        [JsonPropertyName("reasoning")] public object? Reasoning { get; set; }
    }

    class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }


    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static async Task<string?> GetResponseAsync(string prompt, string question, string model = "openrouter-gpt4-1106", float Temperature = 0.7f)
    {
        Logger.Info("Sending request to OpenRouter");
        var proxy = await SettingsProvider.GetValueAsync(BuiltIn.Keys.Proxy);
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (proxy.Value != null)
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxy.Value);
            Logger.Debug($"Configured to use proxy {proxy.Value}");
        }
        using var client = new HttpClient(handler);

        try
        {
            List<(string role, string content)> msg = [("system", prompt), ("user", question)];
            var keySetting = await SettingsProvider.GetValueAsync(BuiltIn.Keys.OpenrouterApiKey);
            if (string.IsNullOrEmpty(keySetting.Value))
            {
                Logger.Error("OpenRouter API key is not set in settings.");
                return null;
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keySetting.Value);

            var payload = new
            {
                model,
                temperature = Temperature,
                messages = msg.ConvertAll(m => new { m.role, m.content })
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");


            var response = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            var responseData = JsonSerializer.Deserialize<OrResponse>(responseBody);

            if (responseData == null || responseData.Choices.Count == 0)
            {
                Logger.Error("No response from OpenRouter.");
                return null;
            }
            return responseData.Choices[0].Message.Content;

        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error while communicating with OpenRouter.");
        }

        return null;
    }
}