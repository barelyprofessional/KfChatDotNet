using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

internal class FxTwitterResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("tweet")] public FxTweet Tweet { get; set; } = new();
}

internal class FxTweet
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("raw_text")] public FxRawText RawText { get; set; } = new();
    [JsonPropertyName("author")] public FxAuthor Author { get; set; } = new();
    [JsonPropertyName("replies")] public int Replies { get; set; }
    [JsonPropertyName("retweets")] public int Retweets { get; set; }
    [JsonPropertyName("likes")] public int Likes { get; set; }

    [JsonPropertyName("created_timestamp")]
    public long CreatedTimestamp { get; set; }

    [JsonPropertyName("views")] public int? Views { get; set; }
    [JsonPropertyName("is_note_tweet")] public bool IsNoteTweet { get; set; }
    [JsonPropertyName("community_note")] public object? CommunityNote { get; set; }
    [JsonPropertyName("lang")] public string Lang { get; set; } = string.Empty;
    [JsonPropertyName("replying_to")] public string? ReplyingTo { get; set; }

    [JsonPropertyName("replying_to_status")]
    public string? ReplyingToStatus { get; set; }

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
    [JsonPropertyName("variants")] public List<FxVariant> Variants { get; set; } = [];
}

internal class FxVariant
{
    [JsonPropertyName("content_type")] public string ContentType { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("bitrate")] public int? Bitrate { get; set; }
}