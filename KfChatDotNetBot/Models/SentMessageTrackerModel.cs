namespace KfChatDotNetBot.Models;

public class SentMessageTrackerModel
{
    // Unique GUID for each message
    public required string Reference { get; set; }
    public required string Message { get; set; }
    public required SentMessageTrackerStatus Status { get; set; }
    public int? ChatMessageId { get; set; }
    // Timespan from when the message was sent until we saw it come back
    public TimeSpan? Delay { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>
    /// If the message was edited, this is the last edit time that Sneedchat sent us
    /// When edited multiple times, it'll be the most recent edit
    /// </summary>
    public DateTimeOffset? LastEdited { get; set; } = null;
}

public enum SentMessageTrackerStatus
{
    WaitingForResponse,
    ResponseReceived,
    // If the bot is blocked from sending the message, e.g. due to suppress chat messages being enabled or length limit
    NotSending,
    // Shouldn't happen normally, it's just set before the bot has made a decision on whether to send or not
    Unknown,
    // Means the chat was disconnected when you attempted to send the message
    ChatDisconnected,
    // Was held in the replay buffer due to a disconnect, but there were too many messages ahead of it and so was culled
    Lost
}