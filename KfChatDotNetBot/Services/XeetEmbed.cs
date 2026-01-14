
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Services;

public class XeetEmbed
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal class FxTwitterResponse
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("tweet")] public FxTweet Tweet { get; set; } = new FxTweet();
    }

    internal class FxTweet
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        [JsonPropertyName("raw_text")] public FxRawText RawText { get; set; } = new FxRawText();
        [JsonPropertyName("author")] public FxAuthor Author { get; set; } = new FxAuthor();
        [JsonPropertyName("replies")] public int Replies { get; set; }
        [JsonPropertyName("retweets")] public int Retweets { get; set; }
        [JsonPropertyName("likes")] public int Likes { get; set; }
        [JsonPropertyName("created_timestamp")] public long CreatedTimestamp { get; set; }
        [JsonPropertyName("views")] public int Views { get; set; }
        [JsonPropertyName("is_note_tweet")] public bool IsNoteTweet { get; set; }
        [JsonPropertyName("community_note")] public object? CommunityNote { get; set; }
        [JsonPropertyName("lang")] public string Lang { get; set; } = string.Empty;
        [JsonPropertyName("replying_to")] public string? ReplyingTo { get; set; }
        [JsonPropertyName("replying_to_status")] public string? ReplyingToStatus { get; set; }
        [JsonPropertyName("media")] public FxMedia? Media { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("quote")] public FxTweet? Quote { get; set; } = null;

        internal bool HasAnyMedia()
        {
            if (Media == null)
                return false;

            if (Media.Photos != null && Media.Photos.Count > 0)
                return true;

            if (Media.Videos != null && Media.Videos.Count > 0)
                return true;

            return false;
        }
    }

    internal class FxRawText
    {
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    internal class FxAuthor
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("screen_name")] public string ScreenName { get; set; } = string.Empty;
    }

    internal class FxMedia
    {
        [JsonPropertyName("photos")] public List<FxPhoto>? Photos { get; set; }
        [JsonPropertyName("videos")] public List<FxVideo>? Videos { get; set; }
    }

    internal class FxPhoto
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
    }

    internal class FxVideo
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("thumbnail_url")] public string ThumbnailUrl { get; set; } = string.Empty;
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("width")] public int Width { get; set; }
        [JsonPropertyName("height")] public int Height { get; set; }
        [JsonPropertyName("format")] public string Format { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("variants")] public List<FxVariant> Variants { get; set; } = new List<FxVariant>();
    }

    internal class FxVariant
    {
        [JsonPropertyName("content_type")] public string ContentType { get; set; } = string.Empty;
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("bitrate")] public int? Bitrate { get; set; }
    }

    internal static string MakeDate(long timestamp)
    {
        var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp * 1000).DateTime;

        var fullTimestamp = dateTime.ToString("h:mm tt · MMM d, yyyy", CultureInfo.InvariantCulture);

        var relativeTimestamp = "";
        if ((DateTime.UtcNow - dateTime).TotalHours < 24)
        {
            var timeAgo = DateTime.UtcNow - dateTime;
            if (timeAgo.TotalHours >= 1)
            {
                int hours = (int)timeAgo.TotalHours;
                relativeTimestamp = $"{hours} hour{(hours > 1 ? "s" : "")} ago";
            }
            else if (timeAgo.TotalMinutes >= 1)
            {
                int minutes = (int)timeAgo.TotalMinutes;
                relativeTimestamp = $"{minutes} minute{(minutes > 1 ? "s" : "")} ago";
            }
            else
            {
                int seconds = (int)timeAgo.TotalSeconds;
                relativeTimestamp = $"{seconds} second{(seconds > 1 ? "s" : "")} ago";
            }
        }

        return fullTimestamp + (relativeTimestamp != "" ? $" ({relativeTimestamp})" : "");
    }

    internal static string MakeMessageFromTweet(FxTweet tweet, string xeetId)
    {
        var mainText = tweet.Text;
        if (mainText.Length > 900)
        {
            mainText = mainText.Substring(0, 900) + "...";
        }
        var message = $"[b]{tweet.Author.Name}[/b] (@{tweet.Author.ScreenName}) - {MakeDate(tweet.CreatedTimestamp)}\n";
        message += $"{mainText}\n";
        message += $"💬 {tweet.Replies}, 🔁 {tweet.Retweets}, ❤️ {tweet.Likes}, 👁️ {tweet.Views}\n";
        message += $"[url={tweet.Url}]X.com[/url] | [url=https://xcancel.com/{tweet.Author.ScreenName}/status/{xeetId}]Xcancel[/url]";

        return message;
    }

    internal static async Task HandleXeet(ChatBot botInstance, MessageModel message)
    {
        var kiwiFarmsUsername = await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsUsername);
        if (message.Author.Username == kiwiFarmsUsername.Value)
        {
            return;
        }

        try
        {
            string messageText = message.Message.Trim();

            var xeetIdMatch = Regex.Match(messageText, @"status/(\d+)");
            if (!xeetIdMatch.Success)
                return;

            var xeetId = xeetIdMatch.Groups[1].Value;
            var api = $"https://api.fxtwitter.com/status/{xeetId}";

            var proxy = await SettingsProvider.GetValueAsync(BuiltIn.Keys.Proxy);
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            if (proxy.Value != null)
            {
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(proxy.Value);
                Logger.Debug($"Configured to use proxy {proxy.Value}");
            }
            using var client = new HttpClient(handler)
            {
                // idk but the requests fail with http 401 while curl works just fine so some of these settings fix it, too lazy to check which one it is.
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");

            using var request = new HttpRequestMessage(HttpMethod.Get, api)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            var tweetData = JsonSerializer.Deserialize<FxTwitterResponse>(responseBody);

            if (tweetData == null || tweetData.Code != 200)
            {
                Logger.Error("Failed to fetch Xeet info or invalid response code.");
                await botInstance.SendChatMessageAsync("Failed to fetch Xeet info.", true);
                return;
            }

            if (tweetData.Tweet.HasAnyMedia())
            {
                // todo: gamba sesh still handles media tweets for now
                return;
            }
            var formattedXeet = MakeMessageFromTweet(tweetData.Tweet, xeetId);
            Logger.Info($"Final xeet: \n{formattedXeet}");
            await botInstance.SendChatMessageAsync(formattedXeet, true);
        }
        catch (Exception ex)
        {
            await botInstance.SendChatMessageAsync("Failed to fetch Xeet info.", true);
            Logger.Error(ex, "Error fetching Xeet info");
            return;
        }
    }
}