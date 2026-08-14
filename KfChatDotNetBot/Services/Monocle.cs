using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

/// <summary>
/// Mints Spur Monocle assessments for the Tartarus gate (see SITE-SECURITY-AND-LOGIN.md §2).
///
/// Since around August 2026 the ttrs challenge POST needs a `monocle` field: a JWE minted by Spur and sealed to a
/// key Tartarus holds. There is no offline path, nothing to port and nothing to forge. There is however a free one:
/// Spur's loader bundle is generated per request and ships an already-minted assessment baked into it as a string
/// constant, which the SDK itself hands to the host page from refreshWithExistingAssessment(). So a plain GET of
/// the loader is enough and no browser is involved.
///
/// This is an undocumented artifact rather than an interface Spur offers, so it can die two ways: Spur stops
/// embedding one (you'd see MonocleAssessmentUnavailableException), or Tartarus starts declining browserless
/// assessments (you'd see monocle_invalid on a freshly fetched one). Either way the fix is a browser fallback that
/// injects the loader and polls window.MCL.getAssessment(), which is not implemented here.
/// </summary>
public class Monocle(string? proxy = null, CancellationToken? cancellationToken = null)
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CancellationToken _ctx = cancellationToken ?? CancellationToken.None;

    // Cached and single-flighted process wide. The assessment is bound to neither the salt, the origin, nor the
    // connection that submits it, and it is good for at least half an hour, so there is no reason for a mass
    // reconnect to turn into a mint storm against a third party.
    private static readonly SemaphoreSlim MintLock = new(1, 1);
    private static string? _assessment;
    private static string? _assessmentSiteKey;
    private static DateTimeOffset _assessmentMintedAt = DateTimeOffset.MinValue;

    // A quoted, dot-separated base64url run, i.e. the shape of a JWE compact serialization. Matching by shape is
    // deliberate: the bundle is minified and the variable holding the assessment is a single letter that changes
    // on every Spur rebuild. Matching by size would be wrong too, as the site key is a 421 character opaque blob
    // sitting in the same bundle. Candidates still have to survive the header check below.
    private static readonly Regex JweCandidateRegex =
        new("[\"'`]([A-Za-z0-9_-]{16,}(?:\\.[A-Za-z0-9_-]*){2,4})[\"'`]", RegexOptions.Compiled);

    /// <summary>
    /// Returns an assessment for the given Monocle site key, minting one if the cache is cold or stale.
    /// </summary>
    /// <param name="siteKey">Value of data-ttrs-monocle-key from the challenge page. Re-read it every time, Spur
    /// rotates it and a stale key mints an assessment the gate will reject.</param>
    /// <param name="forceRefresh">Discard the cached assessment first. Pass this after a monocle_invalid.</param>
    public async Task<string> GetAssessmentAsync(string siteKey, bool forceRefresh = false)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.MonocleLoaderUrl, BuiltIn.Keys.MonocleCacheDuration, BuiltIn.Keys.MonocleProxy,
            BuiltIn.Keys.MonocleUserAgent
        ]);
        var ttl = TimeSpan.FromSeconds(settings[BuiltIn.Keys.MonocleCacheDuration].ToType<int>());

        await MintLock.WaitAsync(_ctx);
        try
        {
            var age = DateTimeOffset.UtcNow - _assessmentMintedAt;
            if (!forceRefresh && _assessment != null && _assessmentSiteKey == siteKey && age < ttl)
            {
                _logger.Debug($"Reusing the cached Monocle assessment, minted {age.TotalSeconds:N0}s ago");
                return _assessment;
            }

            // Deliberately not falling back to a stale assessment on failure. A refusal here costs no proof of
            // work (we mint before solving) whereas submitting a known-bad assessment burns the salt.
            var assessment = await FetchAssessmentAsync(
                settings[BuiltIn.Keys.MonocleLoaderUrl].Value ??
                    throw new InvalidOperationException($"{BuiltIn.Keys.MonocleLoaderUrl} cannot be null"),
                siteKey,
                settings[BuiltIn.Keys.MonocleProxy].Value ?? proxy,
                settings[BuiltIn.Keys.MonocleUserAgent].Value);

            _assessment = assessment;
            _assessmentSiteKey = siteKey;
            _assessmentMintedAt = DateTimeOffset.UtcNow;
            return assessment;
        }
        finally
        {
            MintLock.Release();
        }
    }

    private async Task<string> FetchAssessmentAsync(string loaderUrl, string siteKey, string? mintProxy,
        string? userAgent)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // The loader 301s onto a randomised <random>.mcl.io host and the assessment is on the far side of it
            AllowAutoRedirect = true
        };
        if (mintProxy != null)
        {
            handler.Proxy = new WebProxy(mintProxy);
            handler.UseProxy = true;
        }

        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        if (userAgent != null) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var url = $"{loaderUrl}?tk={Uri.EscapeDataString(siteKey)}";
        var start = DateTime.UtcNow;
        var response = await client.GetAsync(url, _ctx);
        var bundle = await response.Content.ReadAsStringAsync(_ctx);
        if (!response.IsSuccessStatusCode)
        {
            throw new MonocleAssessmentUnavailableException(
                $"Spur's loader returned HTTP {(int)response.StatusCode} for site key {siteKey}");
        }

        _logger.Debug($"Fetched the Monocle loader from {response.RequestMessage?.RequestUri} " +
                      $"({bundle.Length} chars) in {(DateTime.UtcNow - start).TotalMilliseconds:N0}ms");

        var assessment = ExtractAssessment(bundle);
        if (assessment == null)
        {
            throw new MonocleAssessmentUnavailableException(
                "No JWE assessment is baked into Spur's loader bundle. Either Spur stopped embedding one or the " +
                "bundle changed shape. A browser is needed to mint one now (SITE-SECURITY-AND-LOGIN.md §2.2)");
        }

        _logger.Info($"Minted a Monocle assessment without a browser in " +
                     $"{(DateTime.UtcNow - start).TotalMilliseconds:N0}ms ({assessment.Length} chars)");
        return assessment;
    }

    /// <summary>
    /// Pulls the baked assessment out of a loader bundle. Returns null if the bundle has no JWE in it.
    /// </summary>
    internal static string? ExtractAssessment(string bundle)
    {
        foreach (Match match in JweCandidateRegex.Matches(bundle))
        {
            var candidate = match.Groups[1].Value;
            if (IsJweHeader(candidate.Split('.')[0])) return candidate;
        }

        return null;
    }

    // A JWE header decodes to something like {"alg":"ECDH-ES","enc":"A256GCM"}. Requiring `enc` is what separates
    // the assessment from any other dotted base64url-looking literal in the bundle, a JWS header has no `enc`.
    private static bool IsJweHeader(string segment)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(segment)));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("alg", out _) &&
                   document.RootElement.TryGetProperty("enc", out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var b64 = value.Replace('-', '+').Replace('_', '/');
        b64 = (b64.Length % 4) switch
        {
            0 => b64,
            2 => b64 + "==",
            3 => b64 + "=",
            _ => throw new FormatException("Not a valid base64url string")
        };
        return Convert.FromBase64String(b64);
    }
}

/// <summary>
/// Spur would not hand out an assessment. Distinct from the gate rejecting one, see TtrsChallengeRejectedException.
/// </summary>
public class MonocleAssessmentUnavailableException(string message) : Exception(message);
