using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Commands;

[NoPrefixRequired]
public class XeetEmbedCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public List<Regex> Patterns { get; set; } =
    [
        new Regex(@"https?:\/\/x\.com\/(?:\#!\/)?(\w+)\/(?:status|statuses|thread)\/(?<xeetId>\d+)", RegexOptions.IgnoreCase),
        new Regex(@"https?:\/\/twitter\.com\/(?:\#!\/)?(\w+)\/(?:status|statuses|thread)\/(?<xeetId>\d+)",
            RegexOptions.IgnoreCase),
        new Regex(@"https?:\/\/mobile\.twitter\.com\/(?:\#!\/)?(\w+)\/(?:status|statuses|thread)\/(?<xeetId>\d+)",
            RegexOptions.IgnoreCase)
    ];
    public string? HelpText { get; } = "Embed Xeets";
    public UserRight RequiredRight { get; } = UserRight.Loser;
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);

    public RateLimitOptionsModel? RateLimitOptions { get; } = new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(30),
        // Really don't want to get rate-limited by FxTwitter hence global rate-limits
        Flags = RateLimitFlags.Global
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var kiwiFarmsUsername = await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsUsername);
        if (message.Author.Username == kiwiFarmsUsername.Value)
        {
            return;
        }
        var xeetEnabled = await SettingsProvider.GetValueAsync(BuiltIn.Keys.XeetEnabled);
        if (!xeetEnabled.ToBoolean()) return;
        
        var xeetId = arguments["xeetId"].Value;
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
        // Yes, very ghetto but we do need a "real" UA for FxTwitter from my experience
        var ua = await SettingsProvider.GetValueAsync(BuiltIn.Keys.CaptureYtDlpUserAgent);
        
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ua.Value);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var tweetData = await client.GetFromJsonAsync<FxTwitterResponse>(api, cancellationToken: ctx);

        if (tweetData == null)
        {
            throw new InvalidOperationException("tweetData was null");
        }

        if (tweetData.Tweet.HasAnyMedia())
        {
            // todo: gamba sesh still handles media tweets for now
            return;
        }

        var tweet = tweetData.Tweet;
        var mainText = tweet.Text;
        // Sneedchat works off of bytes hence you can't count characters reliably :(
        const int xeetLimit = 900;
        if (mainText.Utf8LengthBytes() > xeetLimit)
        {
            // Copied behavior from SendChatMessageAsync
            mainText = mainText.TruncateBytes(xeetLimit).TrimEnd() + "…";
        }
        var created = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp);
        var response = $"[b][plain]{tweet.Author.Name}[/plain][/b] [plain](@{tweet.Author.ScreenName})[/plain] - {created.Humanize(DateTimeOffset.UtcNow)}[br]" +
                       $"[plain]{mainText}[/plain][br]" +
                       $"💬 {tweet.Replies:N0} 🔁 {tweet.Retweets:N0} ❤️ {tweet.Likes:N0} 👁️ {tweet.Views:N0}[br]" +
                       $"[url={tweet.Url}]X.com[/url] | [url=https://xcancel.com/{tweet.Author.ScreenName}/status/{xeetId}]Xcancel[/url]";
        await botInstance.SendChatMessageAsync(response, true);
    }
}