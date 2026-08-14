using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using NLog;

namespace KfChatDotNetBot.Services;

// Shoutout y a t s for making the original Go implementation I adapted for this
// https://github.com/y-a-t-s/firebird
public class KiwiFlare(string kfDomain, string? proxy = null, CancellationToken? cancellationToken = null)
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CancellationToken _ctx = cancellationToken ?? CancellationToken.None;
    private readonly Random _random = new();

    private HttpClientHandler GetHttpClientHandler()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (proxy != null)
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        return handler;
    }

    public async Task<KiwiFlareChallengeModel?> GetChallenge()
    {
        var handler = GetHttpClientHandler();
        // The 203 sets a short lived (Max-Age=300) staging ttrs_clearance which the gate wants echoed back on the
        // challenge POST, so we need somewhere to catch it
        var container = new CookieContainer();
        handler.CookieContainer = container;
        handler.AllowAutoRedirect = false;
        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(10);
        // The gate is HTTP/1.1 only, it declines an h2 ALPN offer and sends Alt-Svc: clear
        client.DefaultRequestVersion = HttpVersion.Version11;
        var response = await client.GetAsync($"https://{kfDomain}/", _ctx);
        var document = new HtmlDocument();
        document.Load(await response.Content.ReadAsStreamAsync(_ctx));
        var pow = "sssg";
        var challengeData = document.DocumentNode.SelectSingleNode($"//html[@id=\"{pow}\"]");
        if (challengeData == null)
        {
            _logger.Info("challengeData was null. Couldn't find html element with id = sssg, trying ttrs");
            pow = "ttrs";
            challengeData = document.DocumentNode.SelectSingleNode($"//html[@id=\"{pow}\"]");
            if (challengeData == null)
            {
                _logger.Info("challengeData was still null even looking for ttrs");
                return null;
            }
        }

        if (!challengeData.Attributes.Contains($"data-{pow}-challenge")) throw new Exception($"data-{pow}-challenge attribute missing");
        if (!challengeData.Attributes.Contains($"data-{pow}-difficulty")) throw new Exception($"data-{pow}-difficulty attribute missing");
        var patience = TimeSpan.FromMinutes(5);
        // ttrs has no patience value
        if (challengeData.Attributes.Contains("data-sssg-patience"))
        {
            patience = TimeSpan.FromMinutes(Convert.ToDouble(challengeData.Attributes["data-sssg-patience"].Value));
        }
        var salt = challengeData.Attributes[$"data-{pow}-challenge"].Value;
        var difficulty = Convert.ToInt32(challengeData.Attributes[$"data-{pow}-difficulty"].Value);
        // The gate's own worker ships a WASM Argon2id implementation and reads data-ttrs-argon2-{m,t,p}-cost, so
        // this can flip. Fail by name rather than grinding out SHA-256 nonces that get silently rejected.
        var algorithm = challengeData.GetAttributeValue($"data-{pow}-algorithm", "sha256");
        if (algorithm != "sha256") throw new UnsupportedPowAlgorithmException(algorithm);
        var monocleKey = challengeData.Attributes.Contains($"data-{pow}-monocle-key")
            ? challengeData.Attributes[$"data-{pow}-monocle-key"].Value
            : null;
        var stagingClearance = container.GetAllCookies()
            .FirstOrDefault(cookie => cookie.Name == "ttrs_clearance")?.Value;
        _logger.Info($"Got {pow} challenge parameters. IsTtrs = {pow == "ttrs"}, Salt = {salt}, Difficulty = {difficulty}, Patience = {patience.TotalMinutes} minutes");
        _logger.Debug($"Algorithm = {algorithm}, HasMonocleKey = {monocleKey != null}, HasStagingClearance = {stagingClearance != null}");
        return new KiwiFlareChallengeModel
        {
            Salt = salt,
            Difficulty = difficulty,
            Patience = patience,
            IsTtrs = pow == "ttrs",
            Algorithm = algorithm,
            MonocleKey = monocleKey,
            StagingClearance = stagingClearance
        };
    }
    
    private bool TestHash(byte[] hash, int difficulty)
    {
        for (var i = 0; i < difficulty; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = 7 - (i % 8); // MSB first within each byte
            if ((hash[byteIndex] & (1 << bitIndex)) != 0)
            {
                return false;
            }
        }
        return true;
    }

    private Task<KiwiFlareChallengeSolutionModel> ChallengeWorker(KiwiFlareChallengeModel challenge)
    {
        // Must stay non-negative or the gate answers HTTP 500. Seeding well below long.MaxValue keeps it that way
        // as the loop increments, difficulty 16 only needs ~65k iterations to land.
        var nonce = _random.NextInt64(0, 1L << 40);
        while (true)
        {
            nonce++;
            var input = Encoding.UTF8.GetBytes($"{challenge.Salt}{nonce}");
            if (!TestHash(SHA256.HashData(input), challenge.Difficulty)) continue;
            _logger.Info($"Hash passed the test, nonce: {nonce}");
            return Task.FromResult(new KiwiFlareChallengeSolutionModel
            {
                Nonce = nonce,
                Salt = challenge.Salt
            });
        }
    }   

    public async Task<KiwiFlareChallengeSolutionModel> SolveChallenge(KiwiFlareChallengeModel challenge)
    {
        var start = DateTime.UtcNow;
        var worker = Task.Run(() => ChallengeWorker(challenge), _ctx);
        try
        {
            await worker.WaitAsync(challenge.Patience, _ctx);
        }
        catch (Exception e)
        {
            _logger.Error("Caught an exception while trying to solve the challenge. Probably timed out.");
            _logger.Error(e);
            throw;
        }

        if (worker.IsFaulted)
        {
            _logger.Error("Challenge worker faulted");
            _logger.Error(worker.Exception);
            throw new Exception("Challenge worker faulted");
        }

        _logger.Debug($"Worker solved the challenge after {(DateTime.UtcNow - start).TotalMilliseconds} ms");
        return worker.Result;
    }

    public async Task<string> SubmitAnswer(KiwiFlareChallengeSolutionModel solution)
    {
        using var client = new HttpClient(GetHttpClientHandler());
        client.Timeout = TimeSpan.FromSeconds(10);
        var formData = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("a", solution.Salt),
            new("b", solution.Nonce.ToString())
        });

        var response = await client.PostAsync($"https://{kfDomain}/.sssg/api/answer", formData, _ctx);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_ctx);
        if (json.TryGetProperty("error", out var error))
        {
            _logger.Error($"Received error when submitting the answer: {error.GetString()}");
            throw new Exception($"sssg returned an error when submitting the answer: {error.GetString()}");
        }

        if (json.TryGetProperty("auth", out var auth))
        {
            return auth.GetString() ?? throw new InvalidOperationException("Caught null when retrieving auth property");
        }

        _logger.Error("Auth property was missing from sssg response");
        _logger.Error(json.GetRawText());
        throw new Exception($"Auth property was missing from sssg response: {json.GetRawText()}");
    }

    /// <summary>
    /// Submits a solved ttrs challenge and returns the long lived clearance token.
    /// </summary>
    /// <param name="solution">The solved challenge. A rejected POST burns the salt, so any retry needs a fresh
    /// challenge rather than a resubmission of this one.</param>
    /// <param name="monocleAssessment">Spur Monocle assessment from <see cref="Monocle"/>. Without one the gate
    /// answers monocle_required and nothing works at all.</param>
    /// <param name="stagingClearance">The short lived ttrs_clearance the 203 handed out, echoed back.</param>
    public async Task<string> SubmitAnswerTtrs(KiwiFlareChallengeSolutionModel solution, string? monocleAssessment,
        string? stagingClearance)
    {
        var handler = GetHttpClientHandler();
        var container = new CookieContainer();
        handler.CookieContainer = container;
        // A 30x would swallow the Set-Cookie carrying our clearance
        handler.AllowAutoRedirect = false;
        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestVersion = HttpVersion.Version11;
        if (stagingClearance != null)
        {
            container.Add(new Cookie("ttrs_clearance", stagingClearance, "/", kfDomain));
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("salt", solution.Salt),
            new("nonce", solution.Nonce.ToString())
        };
        if (monocleAssessment != null) fields.Add(new KeyValuePair<string, string>("monocle", monocleAssessment));

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{kfDomain}/.ttrs/challenge")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        // The assessment pushes the body past 1KB, which is where some stacks start negotiating 100-continue. The
        // gate answers that with a 400.
        request.Headers.ExpectContinue = false;

        var response = await client.SendAsync(request, _ctx);
        var body = await response.Content.ReadAsStringAsync(_ctx);
        JsonElement json;
        try
        {
            json = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException)
        {
            // The interesting failures come back as 403s with a JSON body, so we parse regardless of status and
            // only complain about the status when there's nothing to read
            _logger.Error($"ttrs returned HTTP {(int)response.StatusCode} with an unparseable body");
            _logger.Error(body[..Math.Min(body.Length, 500)]);
            throw new Exception($"ttrs returned HTTP {(int)response.StatusCode} with an unparseable body");
        }

        if (!json.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            var reason = json.TryGetProperty("reason", out var reasonProperty) ? reasonProperty.GetString() : null;
            _logger.Error($"ttrs didn't accept our solution. HTTP {(int)response.StatusCode}, reason: {reason ?? "(none given)"}");
            throw new TtrsChallengeRejectedException(reason);
        }

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            throw new Exception("ttrs accepted the solution but sent no Set-Cookie header");
        }
        var cookies = setCookies.ToList();
        _logger.Debug($"Set-Cookie header -> {JsonSerializer.Serialize(cookies)}");
        var header = cookies.FirstOrDefault(cookie =>
            cookie.StartsWith("ttrs_clearance=", StringComparison.OrdinalIgnoreCase));
        if (header == null)
        {
            throw new Exception("ttrs accepted the solution but there was no ttrs_clearance in the Set-Cookie headers");
        }
        var token = header.Split("ttrs_clearance=")[1].Split(';')[0].Trim();
        _logger.Debug($"Parsed token from the header: {token}");
        return token;
    }
}

public class KiwiFlareChallengeModel
{
    public required string Salt { get; set; }
    public required int Difficulty { get; set; }
    public required TimeSpan Patience { get; set; }
    public required bool IsTtrs { get; set; }
    /// <summary>
    /// Proof of work algorithm the gate asked for. Only sha256 is implemented
    /// </summary>
    public string Algorithm { get; set; } = "sha256";
    /// <summary>
    /// Spur site key (data-ttrs-monocle-key) to mint an assessment against. Null on sssg and on any ttrs page
    /// served without Monocle enabled. Re-read every time, Spur rotates it.
    /// </summary>
    public string? MonocleKey { get; set; }
    /// <summary>
    /// The short lived ttrs_clearance the 203 set, to be echoed back on the challenge POST
    /// </summary>
    public string? StagingClearance { get; set; }
}

public class KiwiFlareChallengeSolutionModel
{
    public required string Salt { get; set; }
    public required long Nonce { get; set; }
}

/// <summary>
/// The gate refused a solved challenge. The reason matters, see the IsX properties.
/// </summary>
public class TtrsChallengeRejectedException(string? reason)
    : Exception($"ttrs didn't accept our solution with reason: {reason ?? "(none given)"}")
{
    public string? Reason { get; } = reason;

    /// <summary>
    /// Spur read a perfectly valid assessment and judged the connection itself unacceptable, i.e. it thinks we're
    /// a VPN, proxy or datacentre address. Re-minting cannot fix this, only a different egress address can.
    /// </summary>
    public bool IsConnectionRejection => string.Equals(Reason, "MCL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The assessment was missing, stale or unparseable. Worth one retry with a freshly minted one.
    /// </summary>
    public bool IsAssessmentProblem => Reason is "monocle_required" or "monocle_invalid";
}

/// <summary>
/// The gate asked for a proof of work algorithm we don't implement (most likely Argon2id)
/// </summary>
public class UnsupportedPowAlgorithmException(string algorithm)
    : Exception($"The gate asked for the '{algorithm}' proof of work algorithm, only sha256 is implemented")
{
    public string Algorithm { get; } = algorithm;
}