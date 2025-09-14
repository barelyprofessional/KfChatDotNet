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
    DLive,
    KiwiPeerTube
}

public class BaseMetaModel
{
    public CaptureOverridesModel? CaptureOverrides { get; set; } = null;
}

public class CaptureOverridesModel
{
    // Options applicable to YtDlp will not work with Streamlink and vice versa
    // That being said, some options are shared while still being explicitly marked as YtDlp
    // This applies to CaptureYtDlpWorkingDirectory, CaptureYtDlpParentTerminal, and CaptureYtDlpScriptPath
    public string? CaptureYtDlpBinaryPath { get; set; } = null;
    public string? CaptureYtDlpWorkingDirectory { get; set; } = null;
    public string? CaptureYtDlpCookiesFromBrowser { get; set; } = null;
    public string? CaptureYtDlpOutputFormat { get; set; } = null;
    public string? CaptureYtDlpParentTerminal { get; set; } = null;
    public string? CaptureYtDlpScriptPath { get; set; } = null;
    public string? CaptureYtDlpUserAgent { get; set; } = null;
    public string? CaptureStreamlinkBinaryPath { get; set; } = null;
    public string? CaptureStreamlinkOutputFormat { get; set; } = null;
    public string? CaptureStreamlinkRemuxScript { get; set; } = null;
    public string? CaptureStreamlinkTwitchOptions { get; set; } = null;
}

public class KickStreamMetaModel : BaseMetaModel
{
    public required int ChannelId { get; set; }
}

public class PeerTubeMetaModel : BaseMetaModel
{
    public required string AccountName { get; set; }
}