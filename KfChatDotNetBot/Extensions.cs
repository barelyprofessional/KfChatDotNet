using System.Text.RegularExpressions;

namespace KfChatDotNetBot;

public static class Extensions
{
    /// Emotes are encoded like [emote:161238:russW] which translates to -> https://files.kick.com/emotes/161238/fullsize
    public static string TranslateKickEmotes(this string s)
    {
        Regex regex = new Regex(@"\[(.+?):(\d+):(\S+?)\]");
        var matches = regex.Matches(s);
        if (matches.Count == 0)
        {
            return s;
        }

        foreach (Match match in matches)
        {
            // First group is the whole matched string
            // 0 -> [emote:161238:russW]
            // 1 -> emote
            // 2 -> 161238
            // 3 -> russW
            var emoteId = match.Groups[2];
            s = s.Replace(match.Value, $"[img]https://files.kick.com/emotes/{emoteId}/fullsize[/img]");
        }

        return s;
    }
}