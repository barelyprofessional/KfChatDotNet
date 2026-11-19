namespace KfChatDotNetBot.Models;

/// <summary>
/// A chat user as the bot sees them. Ephyra only knows people by principal UUID, so the forum ID in here is something
/// the bot has worked out (avatar URL or a whois lookup) and remembered in the Users table
/// </summary>
public class UserModel
{
    /// <summary>
    /// Forum user ID. 0 if it couldn't be worked out, in which case there's no row for them in the Users table either
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Forum display name. Not HTML encoded (it was with Sneedchat)
    /// </summary>
    public required string Username { get; set; }
    public Uri? AvatarUrl { get; set; }
    /// <summary>
    /// Ephyra principal UUID, what the chat itself identifies the user by
    /// </summary>
    public required string PrincipalUuid { get; set; }
    // Sneedchat used to send this with the user list, Ephyra doesn't have such a thing
    public DateTimeOffset? LastActivity { get; set; }

    /// <summary>
    /// Mention the user so the chat pings them. Same idea as the extension method for UserDbModel
    /// </summary>
    public string FormatUsername()
    {
        return string.IsNullOrEmpty(PrincipalUuid)
            ? $"@{Username}"
            : EphyraChatClient.EphyraBbCode.Mention(PrincipalUuid, Username);
    }
}
