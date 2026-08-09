using System.Security.Cryptography;

namespace KfChatDotNetBot.Extensions;

/// <summary>
/// RFC 6238 TOTP. XenForo's two-step provider is stock: SHA-1, 30 second period, 6 digits, base32 seed.
/// Implemented here rather than pulled in as a package because it's thirty lines.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Generate(string base32Secret, DateTimeOffset? at = null, int digits = 6,
        int periodSeconds = 30)
    {
        var key = Base32Decode(base32Secret);
        if (key.Length == 0) throw new FormatException("The TOTP secret decoded to nothing");
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / periodSeconds;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        // Dynamic truncation, RFC 4226 §5.4
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) |
                     hash[offset + 3];
        return (binary % (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Seconds until the current code rolls over. Used to avoid submitting a code that's about to expire.
    /// </summary>
    public static int SecondsRemaining(DateTimeOffset? at = null, int periodSeconds = 30) =>
        periodSeconds - (int)((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() % periodSeconds);

    private static byte[] Base32Decode(string input)
    {
        // Tolerate however the seed was pasted: lowercase, spaced into groups, padded
        var cleaned = input.Replace(" ", string.Empty).Replace("-", string.Empty).TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bitsInBuffer = 0;
        foreach (var character in cleaned)
        {
            var value = Base32Alphabet.IndexOf(character);
            if (value < 0) throw new FormatException($"'{character}' is not a valid base32 character");
            buffer = (buffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer < 8) continue;
            bitsInBuffer -= 8;
            output.Add((byte)(buffer >> bitsInBuffer));
        }

        return output.ToArray();
    }
}
