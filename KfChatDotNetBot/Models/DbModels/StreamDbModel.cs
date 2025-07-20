namespace KfChatDotNetBot.Models.DbModels;

public class StreamDbModel
{
    public int Id { get; set; }
    /// <summary>
    /// User associated with the stream if any. If none associated, then it'll just say "Somebody has gone live"
    /// </summary>
    public UserDbModel? User { get; set; } = null;
    /// <summary>
    /// Absolute URL of the streamer
    /// </summary>
    public required string StreamUrl { get; set; }
    /// <summary>
    /// Service the streamer is using
    /// </summary>
    public required StreamService Service { get; set; }
    /// <summary>
    /// JSON containing arbitrary data, e.g. social name for Parti, streamer ID for Kick, etc.
    /// </summary>
    public string? Metadata { get; set; } = null;
    /// <summary>
    /// Whether to automatically capture a stream when it goes live using yt-dlp / streamlink
    /// </summary>
    public bool AutoCapture { get; set; } = false;
}

public enum StreamService
{
    Kick,
    Parti,
    DLive
}

public class KickStreamMetaModel
{
    public required int ChannelId { get; set; }
}