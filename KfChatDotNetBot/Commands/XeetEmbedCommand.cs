using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
    private static string LoadingGif = "[img]https://i.ddos.lgbt/u/3sKyHs.webp[/img]";
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

        var loadingMessage = await botInstance.SendChatMessageAsync($"{LoadingGif} Fetching tweet...", true);

        await botInstance.WaitForChatMessageAsync(loadingMessage, TimeSpan.FromSeconds(5), ctx);

        try
        {
            var xeetId = arguments["xeetId"].Value;
            var tweetData = await FetchTweetDataAsync(xeetId, ctx);

            if (tweetData == null)
            {
                throw new InvalidOperationException("tweetData was null");
            }

            var tweet = tweetData.Tweet;

            var mediaUrls = new List<string>();
            if (tweet.HasAnyMedia())
            {
                mediaUrls = await ProcessMediaAsync(tweet, ctx);
            }

            var messages = await BuildTweetMessagesAsync(tweet, xeetId, mediaUrls, ctx);

            if (loadingMessage.ChatMessageId.HasValue)
            {
                await botInstance.KfClient.DeleteMessageAsync(loadingMessage.ChatMessageId.Value);
            }

            if (messages.Count == 0)
            {
                return;
            }

            if (messages.Count > 4)
            {
                // bail, we don't want to spam the chat with giant threads of messages if something goes wrong with the splitting logic
                Logger.Warn($"Aborting sending Xeet embed - message count {messages.Count} exceeds threshold");
                return;
            }

            foreach (var msg in messages)
            {
                await botInstance.SendChatMessageAsync(msg, true);
            }
            // send archive link message
            var url = $"https://nitter.net/{tweet.Author.ScreenName}/status/{xeetId}";
            await botInstance.SendChatMessageAsync(
                $"[url=https://archive.is/submit/?url={url}]Archive Xeet on archive.is[/url]", true);
        }
        catch
        {
            // Delete loading message on error
            if (loadingMessage.ChatMessageId.HasValue)
            {
                await botInstance.KfClient.DeleteMessageAsync(loadingMessage.ChatMessageId.Value);
            }
            throw;
        }
    }

    private async Task<FxTwitterResponse?> FetchTweetDataAsync(string xeetId, CancellationToken ctx)
    {
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

        return await client.GetFromJsonAsync<FxTwitterResponse>(api, cancellationToken: ctx);
    }

    private async Task<List<string>> ProcessMediaAsync(FxTweet tweet, CancellationToken ctx)
    {
        var uploadedUrls = new List<string>();

        if (!await Zipline.IsZiplineEnabled())
        {
            Logger.Warn("Zipline is not enabled, skipping media upload");
            return uploadedUrls;
        }

        try
        {
            if (tweet.Media?.Photos != null)
            {
                foreach (var photo in tweet.Media.Photos)
                {
                    try
                    {
                        var url = await DownloadAndUploadImageAsync(photo.Url, ctx);
                        if (!string.IsNullOrEmpty(url))
                        {
                            uploadedUrls.Add(url);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to process photo: {photo.Url}");
                    }
                }
            }

            if (tweet.Media?.Videos != null)
            {
                foreach (var video in tweet.Media.Videos)
                {
                    try
                    {
                        var url = await DownloadAndConvertVideoAsync(video, ctx);
                        if (!string.IsNullOrEmpty(url))
                        {
                            uploadedUrls.Add(url);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to process video: {video.Url}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error processing media");
        }

        return uploadedUrls;
    }

    private async Task<string?> DownloadAndUploadImageAsync(string imageUrl, CancellationToken ctx)
    {
        using var httpClient = new HttpClient();
        var imageBytes = await httpClient.GetByteArrayAsync(imageUrl, ctx);

        using var imageStream = new MemoryStream(imageBytes);
        var url = await Zipline.Upload(imageStream, new MediaTypeHeaderValue("image/jpeg"), "9h", ctx);

        Logger.Info($"Uploaded image to Zipline: {url}");
        return url;
    }

    private async Task<string?> DownloadAndConvertVideoAsync(FxVideo video, CancellationToken ctx)
    {
        var maxDurationSetting = await SettingsProvider.GetValueAsync(BuiltIn.Keys.XeetMaxVideoDurationSeconds);
        var maxDuration = maxDurationSetting.ToType<int>();

        if (video.Duration > maxDuration)
        {
            Logger.Info($"Skipping video conversion: duration {video.Duration}s exceeds max {maxDuration}s");
            return null;
        }

        // Get the best quality variant
        var bestVariant = video.Variants
            .Where(v => v.Bitrate.HasValue)
            .OrderByDescending(v => v.Bitrate)
            .FirstOrDefault() ?? video.Variants.FirstOrDefault();

        if (bestVariant == null)
        {
            Logger.Warn("No video variant found");
            return null;
        }

        var videoUrl = bestVariant.Url;
        Logger.Info($"Downloading video from {videoUrl} (duration: {video.Duration}s)");

        using var httpClient = new HttpClient();
        var videoBytes = await httpClient.GetByteArrayAsync(videoUrl, ctx);

        var tempVideoPath = Path.Combine(Path.GetTempPath(), $"tweet_video_{Guid.NewGuid()}.mp4");
        var tempWebpPath = Path.Combine(Path.GetTempPath(), $"tweet_video_{Guid.NewGuid()}.webp");

        try
        {
            await File.WriteAllBytesAsync(tempVideoPath, videoBytes, ctx);

            var ffmpegPath = await SettingsProvider.GetValueAsync(BuiltIn.Keys.FFmpegBinaryPath);

            var ffmpegArgs = $"-i \"{tempVideoPath}\" -vf \"fps=10,scale='min(640,iw)':'min(480,ih)':force_original_aspect_ratio=decrease\" -c:v libwebp -lossless 0 -quality 75 -loop 0 -an \"{tempWebpPath}\"";

            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath.Value ?? "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                Logger.Error("Failed to start FFmpeg process");
                return null;
            }

            await process.WaitForExitAsync(ctx);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ctx);
                Logger.Error($"FFmpeg conversion failed: {error}");
                return null;
            }

            await using var webpStream = File.OpenRead(tempWebpPath);
            var url = await Zipline.Upload(webpStream, new MediaTypeHeaderValue("image/webp"), "9h", ctx);

            Logger.Info($"Uploaded video as WebP to Zipline: {url}");
            return url;
        }
        finally
        {
            try
            {
                if (File.Exists(tempVideoPath)) File.Delete(tempVideoPath);
                if (File.Exists(tempWebpPath)) File.Delete(tempWebpPath);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to cleanup temp files");
            }
        }
    }

    private async Task<List<string>> BuildTweetMessagesAsync(FxTweet tweet, string xeetId, List<string> mediaUrls, CancellationToken ctx)
    {
        var headerBuilder = new StringBuilder();
        var bodyBuilder = new StringBuilder();
        var footerBuilder = new StringBuilder();

        // Build header - main tweet author and timestamp (always goes first)
        var created = DateTimeOffset.FromUnixTimeSeconds(tweet.CreatedTimestamp);
        headerBuilder.Append($"[b]{tweet.Author.Name}[/b] (@{tweet.Author.ScreenName}) - {created.Humanize(DateTimeOffset.UtcNow)}[br]");

        // Handle reply chain (if this tweet is a reply)
        if (!string.IsNullOrEmpty(tweet.ReplyingToStatus))
        {
            try
            {
                var replyData = await FetchTweetDataAsync(tweet.ReplyingToStatus, ctx);
                if (replyData?.Tweet != null)
                {
                    var replyTweet = replyData.Tweet;
                    var replyCreated = DateTimeOffset.FromUnixTimeSeconds(replyTweet.CreatedTimestamp);
                    bodyBuilder.Append($"[i]↩️  Replying to:[/i][br]");
                    bodyBuilder.Append($"[b]{replyTweet.Author.Name}[/b] (@{replyTweet.Author.ScreenName}) - {replyCreated.Humanize(DateTimeOffset.UtcNow)}[br]");

                    var replyText = replyTweet.Text;
                    const int replyTextLimit = 250;
                    if (replyText.Utf8LengthBytes() > replyTextLimit)
                    {
                        replyText = replyText.TruncateBytes(replyTextLimit).TrimEnd() + "…";
                    }
                    bodyBuilder.Append($"{replyText}[br][br]");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to fetch reply tweet: {tweet.ReplyingToStatus}");
            }
        }

        // Main tweet text
        var mainText = tweet.Text;
        bodyBuilder.Append($"{mainText}[br]");

        if (mediaUrls.Count > 0)
        {
            foreach (var mediaUrl in mediaUrls)
            {
                bodyBuilder.Append($"[img]{mediaUrl}[/img][br]");
            }
        }

        // Handle quote tweet (if this tweet quotes another)
        if (tweet.Quote != null)
        {
            bodyBuilder.Append("[br]");
            var quoteTweet = tweet.Quote;
            var quoteCreated = DateTimeOffset.FromUnixTimeSeconds(quoteTweet.CreatedTimestamp);
            bodyBuilder.Append($"[i]💬 Quoting:[/i][br]");
            bodyBuilder.Append($"[b]{quoteTweet.Author.Name}[/b] (@{quoteTweet.Author.ScreenName}) - {quoteCreated.Humanize(DateTimeOffset.UtcNow)}[br]");

            var quoteText = quoteTweet.Text;
            const int quoteTextLimit = 250;
            if (quoteText.Utf8LengthBytes() > quoteTextLimit)
            {
                quoteText = quoteText.TruncateBytes(quoteTextLimit).TrimEnd() + "…";
            }
            bodyBuilder.Append($"{quoteText}[br]");
        }

        // Build footer (stats + links) - this will always be on the last message
        footerBuilder.Append($"💬 {tweet.Replies:N0} 🔁 {tweet.Retweets:N0} ❤️ {tweet.Likes:N0} 👁️ {tweet.Views:N0}[br]");
        footerBuilder.Append($"[url={tweet.Url}]X.com[/url] | [url=https://xcancel.com/{tweet.Author.ScreenName}/status/{xeetId}]Xcancel[/url]");

        // Split message if needed, with header always first and footer always last
        var messages = SplitMessageByBytes(headerBuilder.ToString(), bodyBuilder.ToString(), footerBuilder.ToString(), 1024);

        return messages;
    }

    private List<string> SplitMessageByBytes(string header, string bodyContent, string footer, int maxBytes)
    {
        var messages = new List<string>();
        var headerBytes = Encoding.UTF8.GetByteCount(header);
        var footerBytes = Encoding.UTF8.GetByteCount(footer);

        // Check if entire message (header + body + footer) fits in one message
        var fullMessage = header + bodyContent + footer;
        if (Encoding.UTF8.GetByteCount(fullMessage) <= maxBytes)
        {
            messages.Add(fullMessage);
            return messages;
        }

        // Need to split - calculate available space for body content
        // First message needs room for header, last message needs room for footer
        var availableForFirstMessage = maxBytes - headerBytes - 5; // 5 bytes safety margin
        var availableForLastMessage = maxBytes - footerBytes - 5;
        var availableForMiddleMessages = maxBytes - 10; // safety margin

        // Split body by [br] tags
        var parts = bodyContent.Split(new[] { "[br]" }, StringSplitOptions.None);
        var bodyMessages = new List<string>();
        var currentMessage = new StringBuilder();
        var currentBytes = 0;
        var isFirstBodyMessage = true;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLastPart = i == parts.Length - 1;
            var partWithBreak = isLastPart ? part : part + "[br]";
            var partBytes = Encoding.UTF8.GetByteCount(partWithBreak);

            // Determine available space based on whether this is first or middle message
            var availableSpace = isFirstBodyMessage ? availableForFirstMessage : availableForMiddleMessages;

            // If this single part is too large, split it by words
            if (partBytes > availableSpace)
            {
                // Flush current message if it has content
                if (currentBytes > 0)
                {
                    bodyMessages.Add(currentMessage.ToString());
                    currentMessage.Clear();
                    currentBytes = 0;
                    isFirstBodyMessage = false;
                }

                // Split the large part by words
                var words = part.Split(' ');
                var lineSb = new StringBuilder();
                var lineBytes = 0;

                foreach (var word in words)
                {
                    var wordWithSpace = word + " ";
                    var wordBytes = Encoding.UTF8.GetByteCount(wordWithSpace);

                    availableSpace = isFirstBodyMessage ? availableForFirstMessage : availableForMiddleMessages;

                    if (lineBytes + wordBytes > availableSpace)
                    {
                        if (lineSb.Length > 0)
                        {
                            bodyMessages.Add(lineSb.ToString().TrimEnd());
                            lineSb.Clear();
                            lineBytes = 0;
                            isFirstBodyMessage = false;
                        }
                    }

                    lineSb.Append(wordWithSpace);
                    lineBytes += wordBytes;
                }

                if (lineSb.Length > 0)
                {
                    currentMessage.Append(lineSb.ToString().TrimEnd());
                    if (!isLastPart)
                    {
                        currentMessage.Append("[br]");
                    }
                    currentBytes = Encoding.UTF8.GetByteCount(currentMessage.ToString());
                }
                continue;
            }

            // Check if adding this part would exceed the limit
            if (currentBytes + partBytes > availableSpace && currentBytes > 0)
            {
                // Save current message and start new one
                bodyMessages.Add(currentMessage.ToString());
                currentMessage.Clear();
                currentBytes = 0;
                isFirstBodyMessage = false;
            }

            currentMessage.Append(partWithBreak);
            currentBytes += partBytes;
        }

        // Add remaining body content
        if (currentMessage.Length > 0)
        {
            bodyMessages.Add(currentMessage.ToString());
        }

        // Assemble final messages with header first and footer last
        if (bodyMessages.Count > 0)
        {
            // First message: header + first body part
            messages.Add(header + bodyMessages[0]);

            // Middle messages (if any)
            for (int i = 1; i < bodyMessages.Count; i++)
            {
                messages.Add(bodyMessages[i]);
            }

            // Add footer to the last message
            messages[messages.Count - 1] = messages[messages.Count - 1] + footer;
        }
        else
        {
            // Only header and footer
            messages.Add(header + footer);
        }

        return messages;
    }
}