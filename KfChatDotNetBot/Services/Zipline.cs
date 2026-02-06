using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public static class Zipline
{
    public static async Task<string?> Upload(Stream content, MediaTypeHeaderValue mimeType, string? expiration = null, CancellationToken ct = default)
    {
        using var formContent = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = mimeType;
        formContent.Add(fileContent, "upload", Money.GenerateEventId());
        var url = await DoUpload(formContent, expiration, ct);
        return url;
    }
    
    public static async Task<string?> Upload(string content, MediaTypeHeaderValue mimeType, string? expiration = null, CancellationToken ct = default)
    {
        using var formContent = new MultipartFormDataContent();
        var fileContent = new StringContent(content);
        fileContent.Headers.ContentType = mimeType;
        formContent.Add(fileContent, "upload", Money.GenerateEventId());
        var url = await DoUpload(formContent, expiration, ct);
        return url;
    }
    
    private static async Task<string?> DoUpload(MultipartFormDataContent content, string? expiration = null, CancellationToken ct = default)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.Proxy, BuiltIn.Keys.ZiplineUrl, BuiltIn.Keys.ZiplineKey
            ]);

        if (settings[BuiltIn.Keys.ZiplineKey].Value == null)
        {
            throw new InvalidOperationException("ZiplineKey is not defined");
        }
        
        var handler = new HttpClientHandler();
        if (settings[BuiltIn.Keys.Proxy].Value != null)
        {
            handler.Proxy = new WebProxy(settings[BuiltIn.Keys.Proxy].Value);
            handler.UseProxy = true;
        }

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", settings[BuiltIn.Keys.ZiplineKey].Value);
        if (expiration != null)
        {
            client.DefaultRequestHeaders.Add("x-zipline-deletes-at", expiration);
        }

        var response = await client.PostAsync($"{settings[BuiltIn.Keys.ZiplineUrl].Value}/api/upload", content, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        string url;
        try
        {
            url = json.GetProperty("files")[0].GetProperty("url").GetString() ??
                  throw new InvalidOperationException("Caught null when grabbing Zipline result");
        }
        catch (Exception e)
        {
            logger.Error("Caught exception when attempting to upload to Zipline. Raw JSON response followed by exception");
            logger.Error(json.GetRawText);
            logger.Error(e);
            throw;
        }

        return url;
    }

    // public static async Task<string> RehostFile(string url)
    // {
    //     
    // }

    public static async Task<bool> IsZiplineEnabled()
    {
        var key = await SettingsProvider.GetValueAsync(BuiltIn.Keys.ZiplineKey);
        return !string.IsNullOrEmpty(key.Value);
    }
}