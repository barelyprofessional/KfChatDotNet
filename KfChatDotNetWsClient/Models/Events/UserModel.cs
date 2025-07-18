namespace KfChatDotNetWsClient.Models.Events;

public class UserModel
{
    public int Id { get; set; }
    /// <summary>
    /// Forum display name. Note that it'll be HTML encoded
    /// </summary>
    public required string Username { get; set; }
    public Uri? AvatarUrl { get; set; }
    // Unset if it's related to a chat message
    public DateTimeOffset? LastActivity { get; set; }
}