using System.Text;
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
    
    public static IEnumerable<string> SplitMessage(this string s, int partLength = 500, int partLimit = 5)
    {
        if (s == null)
            throw new ArgumentNullException(nameof(s));
        if (partLength <= 0)
            throw new ArgumentException("Part length has to be positive.", nameof(partLength));

        var parts = 0;
        for (var i = 0; i < s.Length; i += partLength)
        {
            parts++;
            if (parts > partLimit) break;
            yield return s.Substring(i, Math.Min(partLength, s.Length - i));
        }
    }

    /// <summary>
    /// Split messages to x number of bytes while avoiding splitting mid-word where possible
    /// </summary>
    /// <param name="s">String that should get split</param>
    /// <param name="partLengthBytes">Length limit, no part should be > than the number of bytes specified</param>
    /// <param name="partLimit">Limit for how many parts to return (returns first n elements). Set to 0 to disable.</param>
    /// <returns></returns>
    public static List<string> FancySplitMessage(this string s, int partLengthBytes = 1023, int partLimit = 5)
    {
        var output = new List<string>();
        var part = string.Empty;
        foreach (var word in s.Split(' '))
        {
            if (word.Utf8LengthBytes() > partLengthBytes)
            {
                // Add the part already in memory if there is one
                if (part != string.Empty)
                {
                    output.Add(part.TrimEnd());
                    part = string.Empty;
                }
                // Breaks into chunks of x size which will break really long URLs etc. but no other way really
                output.AddRange(word.ChunkBytes(partLengthBytes));
                continue;
            }
            
            if (part.Utf8LengthBytes() + word.Utf8LengthBytes() > partLengthBytes)
            {
                // TrimEnd() to remove trailing spaces
                output.Add(part.TrimEnd());
                part = word + " ";
                continue;
            }

            part += word + " ";
        }

        // Add on whatever remains
        if (part != string.Empty)
        {
            output.Add(part.TrimEnd());
        }

        if (partLimit != 0 && output.Count > partLimit)
        {
            return output.Take(partLimit).ToList();
        }
        
        return output;
    }

    public static int Utf8LengthBytes(this string s)
    {
        return Encoding.UTF8.GetByteCount(s);
    }

    public static IEnumerable<string> ChunkBytes(this string input, int bytesPerChunk)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
    
        for (var i = 0; i < bytes.Length; i += bytesPerChunk)
        {
            var chunkSize = Math.Min(bytesPerChunk, bytes.Length - i);
            yield return Encoding.UTF8.GetString(bytes, i, chunkSize);
        }
    }


    public static string TruncateBytes(this string s, int limitBytes)
    {
        return Encoding.UTF8.GetString(
            Encoding.UTF8.GetBytes(s)
                .Take(limitBytes)
                .ToArray()
        ).TrimEnd();
    }
}