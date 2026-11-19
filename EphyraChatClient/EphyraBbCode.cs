using System.Text.RegularExpressions;

namespace EphyraChatClient;

/// <summary>
/// Helpers for the bits of BBCode that are specific to Ephyra
/// </summary>
public static partial class EphyraBbCode
{
    /// <summary>
    /// Build a mention which will actually ping the user. What the web client produces when you pick someone from the
    /// @ autocomplete
    /// </summary>
    /// <param name="principalUuid">Principal UUID of whoever you're mentioning</param>
    /// <param name="displayName">Name to show. Purely decoration, the UUID is what counts</param>
    public static string Mention(string principalUuid, string displayName)
    {
        // Brackets would open or close tags and line breaks aren't allowed in the body of the tag
        var name = WhitespaceRegex().Replace(displayName.Replace("[", "").Replace("]", ""), " ").Trim();
        return $"[user={principalUuid}]{name}[/user]";
    }

    /// <summary>
    /// Turn [user=uuid]Name[/user] back into @Name, which is what you'd have received from Sneedchat
    /// </summary>
    public static string MentionsToPlainText(string bbcode)
    {
        return bbcode.Contains("[user=", StringComparison.OrdinalIgnoreCase)
            ? MentionRegex().Replace(bbcode, match => $"@{match.Groups["name"].Value}")
            : bbcode;
    }

    /// <summary>
    /// Principal UUIDs of everyone mentioned in the BBCode along with the name they were mentioned by
    /// </summary>
    public static List<(string PrincipalUuid, string Name)> ExtractMentions(string bbcode)
    {
        return MentionRegex().Matches(bbcode)
            .Select(match => (match.Groups["uuid"].Value.ToLowerInvariant(), match.Groups["name"].Value)).ToList();
    }

    [GeneratedRegex(@"\[user=(?<uuid>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\](?<name>[^\[\]\n]*)\[/user\]", RegexOptions.IgnoreCase)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
