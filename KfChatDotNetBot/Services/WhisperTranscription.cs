using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// OpenAI Whisper API integration for voice message transcription.
///
/// Used to transcribe Discord voice messages from monitored users.
/// Downloads audio from a URL and sends it to the Whisper API for speech-to-text.
///
/// API Documentation: https://platform.openai.com/docs/api-reference/audio/createTranscription
/// API Endpoint: https://api.openai.com/v1/audio/transcriptions
///
/// Supported formats: mp3, mp4, mpeg, mpga, m4a, wav, webm, ogg, flac
/// Max file size: 25MB (configurable via Whisper.MaxFileSize setting)
///
/// Configuration:
/// - Whisper.ApiKey: OpenAI API key for Whisper (required)
/// - Whisper.Enabled: Feature toggle (default: false)
/// - Whisper.Endpoint: API endpoint (optional override)
/// - Whisper.Model: Model name (default: whisper-1)
/// - Whisper.MaxFileSize: Max download size in bytes (default: 25MB)
/// - Proxy: Global proxy setting (optional)
/// </summary>
public static class WhisperTranscription
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public class TranscriptionResponse
    {
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Downloads audio from a URL and transcribes it via the OpenAI Whisper API.
    ///
    /// Flow:
    /// 1. Validate settings (API key, feature enabled)
    /// 2. Download audio from URL with size limit enforcement
    /// 3. Send multipart/form-data request to Whisper endpoint
    /// 4. Return transcribed text
    ///
    /// Returns null on any failure (missing config, download error, API error).
    /// All errors are logged via NLog.
    /// </summary>
    public static async Task<string?> TranscribeFromUrlAsync(string url, string fileName, CancellationToken ct = default)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.WhisperApiKey,
            BuiltIn.Keys.WhisperEnabled,
            BuiltIn.Keys.WhisperEndpoint,
            BuiltIn.Keys.WhisperModel,
            BuiltIn.Keys.WhisperMaxFileSize,
            BuiltIn.Keys.Proxy
        ]);

        if (!settings[BuiltIn.Keys.WhisperEnabled].ToBoolean())
        {
            Logger.Debug("Whisper transcription is disabled");
            return null;
        }

        var apiKey = settings[BuiltIn.Keys.WhisperApiKey].Value;
        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.Error("OpenAI API key is not set, cannot transcribe");
            return null;
        }

        var maxFileSize = long.Parse(settings[BuiltIn.Keys.WhisperMaxFileSize].Value ?? "26214400");
        var endpoint = settings[BuiltIn.Keys.WhisperEndpoint].Value
                       ?? "https://api.openai.com/v1/audio/transcriptions";
        var model = settings[BuiltIn.Keys.WhisperModel].Value ?? "whisper-1";

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
            // Download the audio file
            Logger.Info($"Downloading voice message from {url}");
            using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, url);
            using var downloadResponse = await client.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            downloadResponse.EnsureSuccessStatusCode();

            if (downloadResponse.Content.Headers.ContentLength > maxFileSize)
            {
                Logger.Warn($"Voice message too large: {downloadResponse.Content.Headers.ContentLength} bytes (max {maxFileSize})");
                return null;
            }

            // Read into memory with size limit
            await using var responseStream = await downloadResponse.Content.ReadAsStreamAsync(ct);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await responseStream.ReadAsync(buffer, ct)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxFileSize)
                {
                    Logger.Warn($"Voice message exceeded max size during download ({maxFileSize} bytes)");
                    return null;
                }
                memoryStream.Write(buffer, 0, bytesRead);
            }

            Logger.Info($"Downloaded {totalRead} bytes, sending to Whisper API");
            memoryStream.Position = 0;

            // Build multipart request for Whisper API
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var formContent = new MultipartFormDataContent();
            var fileContent = new StreamContent(memoryStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                downloadResponse.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
            formContent.Add(fileContent, "file", fileName);
            formContent.Add(new StringContent(model), "model");

            var response = await client.PostAsync(endpoint, formContent, ct);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var transcription = JsonSerializer.Deserialize<TranscriptionResponse>(responseBody);

            if (string.IsNullOrWhiteSpace(transcription?.Text))
            {
                Logger.Warn("Whisper API returned empty transcription");
                return null;
            }

            Logger.Info($"Transcription received: {transcription.Text.Length} characters");
            return transcription.Text;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during Whisper transcription");
            return null;
        }
    }
}
