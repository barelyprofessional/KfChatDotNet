using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Settings;

namespace KfChatDotNetBot.Services;

public static class YouTubeApi
{
    private static async Task<HttpClient> GetHttpClient()
    {
        var settings = 
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.YouTubeApiKey, BuiltIn.Keys.Proxy]);
        if (settings[BuiltIn.Keys.YouTubeApiKey].Value == null)
            throw new InvalidOperationException("YouTube API key has not been configured");
        var handler = new HttpClientHandler();
        if (settings[BuiltIn.Keys.Proxy].Value != null)
        {
            handler.Proxy = new WebProxy(settings[BuiltIn.Keys.Proxy].Value);
            handler.UseProxy = true;
        }
        handler.AutomaticDecompression = DecompressionMethods.All;
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KenoGPT", "1.0"));
        client.DefaultRequestHeaders.Add("X-Goog-Api-Key", settings[BuiltIn.Keys.YouTubeApiKey].Value);
        client.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static async Task<YouTubeApiModels.ItemModel?> GetVideoDetails(string id)
    {
        var client = await GetHttpClient();
        var response =
            await client.GetFromJsonAsync<YouTubeApiModels.ContentDetailsRoot>($"videos?part=snippet&id={id}");
        if (response?.Items.Count == 0) return null;
        return response?.Items[0];
    }
}