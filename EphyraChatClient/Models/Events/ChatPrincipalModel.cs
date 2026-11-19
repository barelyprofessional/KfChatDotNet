using System.Text.RegularExpressions;

namespace EphyraChatClient.Models.Events;

/// <summary>
/// A chat user. Ephyra only ever identifies people by principal UUID, forum user IDs aren't part of the protocol
/// </summary>
public partial class ChatPrincipalModel
{
    public required string PrincipalUuid { get; set; }
    /// <summary>
    /// Forum display name. Unlike Sneedchat this is NOT HTML encoded
    /// </summary>
    public required string DisplayName { get; set; }
    public Uri? AvatarUrl { get; set; }
    public List<string> Badges { get; set; } = [];

    /// <summary>
    /// XenForo avatars live at /data/avatars/{size}/{id / 1000}/{id}.ext so the forum user ID can be fished out of the
    /// URL. Null if the user has no avatar or it's hosted somewhere which doesn't follow that scheme (e.g. Gravatar)
    /// </summary>
    public int? ForumUserIdFromAvatar => ForumUserIdFromAvatarUrl(AvatarUrl?.OriginalString);

    public static int? ForumUserIdFromAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl)) return null;
        var match = AvatarRegex().Match(avatarUrl);
        if (!match.Success) return null;
        // The group is the folder (id / 1000), so make sure it lines up to avoid being fooled by a lookalike URL
        if (!int.TryParse(match.Groups["id"].Value, out var id) || !int.TryParse(match.Groups["group"].Value, out var group)) return null;
        return id / 1000 == group ? id : null;
    }

    [GeneratedRegex(@"/data/avatars/[a-z0-9]+/(?<group>\d+)/(?<id>\d+)\.[a-z0-9]+(?:\?|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarRegex();
}
