namespace KfChatDotNetBot.Models;

public class RateLimitBucketEntryModel
{
    /// <summary>
    /// Database user ID of the user whose entry this belongs to
    /// </summary>
    public required int UserId { get; set; }
    /// <summary>
    /// When the entry was created in the bucket
    /// </summary>
    public required DateTimeOffset EntryCreated { get; set; }
    /// <summary>
    /// When the entry is expected to expire based on the command's window
    /// </summary>
    public required DateTimeOffset EntryExpires { get; set; }
    /// <summary>
    /// String representation of the command using ICommand.GetType().Name
    /// </summary>
    public required string CommandInvoked { get; set; }
    /// <summary>
    /// Hashed contents of the message for if UseEntireMessage is enabled
    /// </summary>
    public required string MessageHash { get; set; }
}

public class RateLimitOptionsModel
{
    /// <summary>
    /// Window of time to count an invocation towards the rate limit
    /// </summary>
    public required TimeSpan Window { get; set; }
    /// <summary>
    /// Maximum number of permitted invocations within the window before triggering the rate limit
    /// </summary>
    public required int MaxInvocations { get; set; }

    /// <summary>
    /// Optional set of flags to configure the behavior of the rate limiter
    /// </summary>
    public RateLimitFlags Flags { get; set; } = RateLimitFlags.None;
}

public class IsRateLimitedModel
{
    /// <summary>
    /// Is the user's request rate limited?
    /// </summary>
    public required bool IsRateLimited { get; set; }
    /// <summary>
    /// When the oldest entry expires so users know when they can next use the command
    /// This is set to null if the user is not rate limited
    /// </summary>
    public DateTimeOffset? OldestEntryExpires { get; set; }
}

[Flags]
public enum RateLimitFlags
{
    /// <summary>
    /// Placeholder for the default value
    /// </summary>
    None,
    /// <summary>
    /// Silently ignore a user when they trigger a rate limit
    /// </summary>
    NoResponse,
    /// <summary>
    /// The default behavior is to rate limit based on command invoked.
    /// UseEntireMessage changes it to consider dissimilar messages which invoke
    /// the same command as being separate for the purposes of rate limiting.
    /// With this, only identical messages count towards the rate limit.
    /// </summary>
    UseEntireMessage,
    /// <summary>
    /// The rate limit is global instead of applying per-user
    /// </summary>
    Global,
    /// <summary>
    /// Exempt users with a higher than default level from rate limiting
    /// </summary>
    ExemptPrivilegedUsers,
    /// <summary>
    /// Do not automatically clean up the cooldown response sent to a user
    /// </summary>
    NoAutoDeleteCooldownResponse
}