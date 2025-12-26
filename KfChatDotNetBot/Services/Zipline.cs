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
        using var formContent = new MultipartFormDataContent();
        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = mimeType;
        formContent.Add(fileContent, "upload", Money.GenerateEventId());
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", settings[BuiltIn.Keys.ZiplineKey].Value);
        if (expiration != null)
        {
            client.DefaultRequestHeaders.Add("x-zipline-expiration", expiration);
        }

        var response = await client.PostAsync($"{settings[BuiltIn.Keys.ZiplineUrl].Value}/api/upload", formContent, ct);
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
}