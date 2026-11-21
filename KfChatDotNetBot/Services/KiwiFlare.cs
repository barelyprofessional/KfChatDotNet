using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HtmlAgilityPack;
using NLog;

namespace KfChatDotNetBot.Services;

// Shoutout y a t s for making the original Go implementation I adapted for this
// https://github.com/y-a-t-s/firebird
/// <summary>
/// Solver for Tartarus (ttrs), the "Checking your browser" page the forum answers with (as a 203) until you have a
/// ttrs_clearance cookie. It's a proof of work, which these days is followed by a bandwidth challenge
/// </summary>
public class KiwiFlare(string kfDomain, string? proxy = null, CancellationToken? cancellationToken = null)
{
    /// <summary>Name of the cookie a solved challenge earns</summary>
    public const string ClearanceCookieName = "ttrs_clearance";
    // The page gives no time limit for the proof of work, so this is ours
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromMinutes(5);
    // What the browser allows for the bandwidth challenge if the offer doesn't come with a timeout of its own
    private static readonly TimeSpan DefaultBandwidthTimeout = TimeSpan.FromMinutes(2);
    // Number.MAX_SAFE_INTEGER, the browser picks its first nonce from below this
    private const long MaxSafeInteger = 9007199254740991;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CancellationToken _ctx = cancellationToken ?? CancellationToken.None;

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

    /// <summary>
    /// Load the front page without any cookies and read the challenge off it
    /// </summary>
    /// <returns>Null if what came back isn't a challenge page</returns>
    public async Task<KiwiFlareChallengeModel?> GetChallenge()
    {
        using var client = new HttpClient(GetHttpClientHandler());
        client.Timeout = TimeSpan.FromSeconds(10);
        using var response = await client.GetAsync($"https://{kfDomain}/", _ctx);
        var document = new HtmlDocument();
        document.Load(await response.Content.ReadAsStreamAsync(_ctx));
        var challengeData = document.DocumentNode.SelectSingleNode("//html[@id=\"ttrs\"]");
        if (challengeData == null)
        {
            _logger.Info("challengeData was null. Couldn't find html element with id = ttrs");
            return null;
        }

        var salt = challengeData.Attributes["data-ttrs-challenge"]?.Value ??
                   throw new Exception("data-ttrs-challenge attribute missing");
        var difficulty = Convert.ToInt32(challengeData.Attributes["data-ttrs-difficulty"]?.Value ??
                                         throw new Exception("data-ttrs-difficulty attribute missing"));
        // The browser takes it to be sha256 if the page doesn't say
        var algorithm = challengeData.Attributes["data-ttrs-algorithm"]?.Value ?? "sha256";
        _logger.Info($"Got ttrs challenge parameters. Salt = {salt}, Difficulty = {difficulty}, Algorithm = {algorithm}");
        if (challengeData.Attributes.Contains("data-ttrs-monocle-key"))
        {
            // A browser would run Spur's Monocle and send its assessment along with the answer. It also carries on
            // without one if Monocle doesn't load, which is the only option we've got
            _logger.Warn("Challenge page wants a Monocle assessment. Can't produce one, so the answer will go in without it and may get refused");
        }

        return new KiwiFlareChallengeModel
        {
            Salt = salt,
            Difficulty = difficulty,
            Algorithm = algorithm
        };
    }

    // Same test as the page's worker: the first 32 bits of the hash have to start with at least this many zero bits
    private static bool TestHash(ReadOnlySpan<byte> hash, int difficulty)
    {
        return BitOperations.LeadingZeroCount(BinaryPrimitives.ReadUInt32BigEndian(hash)) >= difficulty;
    }

    private KiwiFlareChallengeSolutionModel ChallengeWorker(KiwiFlareChallengeModel challenge, CancellationToken ct)
    {
        // Like the browser, start somewhere random and count up from there
        var nonce = Random.Shared.NextInt64(MaxSafeInteger);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            nonce++;
            var input = Encoding.UTF8.GetBytes($"{challenge.Salt}{nonce}");
            if (!TestHash(SHA256.HashData(input), challenge.Difficulty)) continue;
            _logger.Info($"Hash passed the test, nonce: {nonce}");
            return new KiwiFlareChallengeSolutionModel
            {
                Nonce = nonce,
                Salt = challenge.Salt
            };
        }
    }

    /// <summary>
    /// Do the proof of work: find a nonce which hashes together with the salt to something with enough leading zeroes
    /// </summary>
    public async Task<KiwiFlareChallengeSolutionModel> SolveChallenge(KiwiFlareChallengeModel challenge)
    {
        if (challenge.Algorithm != "sha256")
        {
            // The page can also ask for argon2id (data-ttrs-argon2-m-cost / t-cost / p-cost). Never seen it switched on
            throw new NotSupportedException($"ttrs wants the proof of work done with {challenge.Algorithm}, only sha256 is implemented");
        }

        var start = DateTime.UtcNow;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_ctx);
        cts.CancelAfter(SolveTimeout);
        try
        {
            var solution = await Task.Run(() => ChallengeWorker(challenge, cts.Token), cts.Token);
            _logger.Debug($"Worker solved the challenge after {(DateTime.UtcNow - start).TotalMilliseconds} ms");
            return solution;
        }
        catch (OperationCanceledException) when (!_ctx.IsCancellationRequested)
        {
            _logger.Error($"Gave up on the challenge as it wasn't solved within {SolveTimeout.TotalMinutes} minutes");
            throw new TimeoutException($"Challenge wasn't solved within {SolveTimeout.TotalMinutes} minutes");
        }
    }

    /// <summary>
    /// Hand the solution in. If ttrs answers that with a bandwidth challenge, that gets done as well and the solution
    /// is handed in a second time along with the receipt for it
    /// </summary>
    /// <returns>Value for the ttrs_clearance cookie</returns>
    public async Task<string> SubmitAnswer(KiwiFlareChallengeSolutionModel solution)
    {
        var (answer, clearance) = await PostAnswer(solution);
        if (clearance != null) return clearance;
        if (answer.Next != "bandwidth")
        {
            _logger.Error($"ttrs didn't accept our solution with reason: {answer.Reason}");
            throw new Exception($"ttrs didn't accept our solution with reason: {answer.Reason}");
        }

        if (string.IsNullOrEmpty(answer.Token) || answer.BandwidthSize is null or <= 0 || answer.BandwidthTimeoutSecs is <= 0)
        {
            throw new Exception("ttrs returned an invalid bandwidth offer");
        }

        var timeout = answer.BandwidthTimeoutSecs == null
            ? DefaultBandwidthTimeout
            : TimeSpan.FromSeconds(answer.BandwidthTimeoutSecs.Value);
        var receipt = await RunBandwidthChallenge(answer.Token, answer.BandwidthSize.Value, timeout);
        (answer, clearance) = await PostAnswer(solution, receipt);
        if (clearance != null) return clearance;
        // It only gets offered the once. The browser gives up here too
        _logger.Error($"ttrs didn't accept our solution after the bandwidth challenge. Reason: {answer.Reason}, next: {answer.Next}");
        throw new Exception($"ttrs didn't accept our solution after the bandwidth challenge. Reason: {answer.Reason}, next: {answer.Next}");
    }

    private async Task<(KiwiFlareAnswerModel Answer, string? Clearance)> PostAnswer(
        KiwiFlareChallengeSolutionModel solution, string? bandwidthReceipt = null)
    {
        var handler = GetHttpClientHandler();
        // The clearance comes back as a cookie, which is read straight off the header instead of going through a
        // cookie container so it doesn't matter what domain it was set for
        handler.UseCookies = false;
        handler.AllowAutoRedirect = false;
        using var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(10);
        var formData = new List<KeyValuePair<string, string>>
        {
            new("salt", solution.Salt),
            new("nonce", solution.Nonce.ToString())
        };
        if (bandwidthReceipt != null) formData.Add(new KeyValuePair<string, string>("bandwidth_receipt", bandwidthReceipt));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{kfDomain}/.ttrs/challenge");
        request.Content = new FormUrlEncodedContent(formData);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("Origin", $"https://{kfDomain}");
        request.Headers.Referrer = new Uri($"https://{kfDomain}/");
        using var response = await client.SendAsync(request, _ctx);
        var body = await response.Content.ReadAsStringAsync(_ctx);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // What the browser reloads the page for
            throw new Exception($"ttrs says the challenge has expired or is invalid: {body}");
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            // 403 comes with a JSON body whose action says why (monocle_unavailable, monocle_blocked), 413 is too much data
            throw new Exception($"ttrs returned {(int)response.StatusCode} when submitting the answer: {body}");
        }

        KiwiFlareAnswerModel? answer;
        try
        {
            answer = JsonSerializer.Deserialize<KiwiFlareAnswerModel>(body);
        }
        catch (JsonException e)
        {
            throw new Exception($"Couldn't make sense of what ttrs returned when submitting the answer: {body}", e);
        }

        if (answer == null) throw new Exception("ttrs returned null when submitting the answer");
        if (!answer.Success) return (answer, null);

        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
        _logger.Debug($"Set-Cookie header -> {JsonSerializer.Serialize(setCookies)}");
        var prefix = $"{ClearanceCookieName}=";
        var header = setCookies.FirstOrDefault(cookie => cookie.StartsWith(prefix));
        if (header == null) throw new Exception($"ttrs accepted our solution but didn't set {ClearanceCookieName}");
        var token = header[prefix.Length..].Split(';')[0];
        _logger.Debug($"Parsed token from the header: {token}");
        return (answer, token);
    }

    /// <summary>
    /// The bandwidth challenge: ttrs sends a pile of junk down a WebSocket to see that we can actually take it, then
    /// wants proof that it all arrived
    /// </summary>
    /// <param name="token">Token from the bandwidth offer</param>
    /// <param name="size">How many bytes the offer said to expect. Only used for reporting progress</param>
    /// <param name="timeout">How long to give it before giving up</param>
    /// <returns>Receipt to hand in with the answer</returns>
    private async Task<string> RunBandwidthChallenge(string token, long size, TimeSpan timeout)
    {
        _logger.Info($"ttrs wants a bandwidth challenge done, {size:N0} bytes to receive within {timeout.TotalSeconds:N0} seconds");
        var start = DateTime.UtcNow;
        using var socket = new ClientWebSocket();
        if (proxy != null) socket.Options.Proxy = new WebProxy(proxy);
        socket.Options.SetRequestHeader("Origin", $"https://{kfDomain}");
        // Browsers don't send keepalives off their own bat, so neither do we
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_ctx);
        cts.CancelAfter(timeout);
        try
        {
            // proof=sha256 picks the scheme below. Without it ttrs expects one BLAKE2b hash over the whole stream,
            // which only exists for browsers that have no Web Crypto
            await socket.ConnectAsync(
                new Uri($"wss://{kfDomain}/.ttrs/captcha/bandwidth?token={Uri.EscapeDataString(token)}&proof=sha256"),
                cts.Token);

            // The proof is the SHA-256 of the SHA-256 of every binary message, in the order they arrived. A message
            // can turn up in several pieces, it's the whole message that gets hashed
            using var proof = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var message = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var text = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long received = 0;
            var lastReported = 0;
            var proofSent = false;
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new Exception($"ttrs closed the bandwidth WebSocket before completion ({(int?)socket.CloseStatus} {socket.CloseStatusDescription}), got {received:N0} bytes");
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    message.AppendData(buffer.AsSpan(0, result.Count));
                    received += result.Count;
                    if (result.EndOfMessage) proof.AppendData(message.GetHashAndReset());
                    var percent = (int)Math.Min(100, received * 100 / size);
                    if (percent < lastReported + 25) continue;
                    lastReported = percent - percent % 25;
                    _logger.Debug($"Bandwidth challenge: {received:N0} of {size:N0} bytes ({percent}%)");
                    continue;
                }

                text.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var json = Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
                text.SetLength(0);
                KiwiFlareBandwidthMessageModel? status;
                try
                {
                    status = JsonSerializer.Deserialize<KiwiFlareBandwidthMessageModel>(json);
                }
                catch (JsonException)
                {
                    // Browser ignores anything it can't parse
                    _logger.Debug($"Ignoring a message from the bandwidth WebSocket which isn't JSON: {json}");
                    continue;
                }

                switch (status?.Status)
                {
                    case "verify" when !proofSent:
                        proofSent = true;
                        var answer = JsonSerializer.Serialize(new Dictionary<string, string>
                            { ["proof"] = Convert.ToHexStringLower(proof.GetHashAndReset()) });
                        _logger.Debug($"Received {received:N0} bytes in {(DateTime.UtcNow - start).TotalSeconds:N1} seconds, sending the proof");
                        await socket.SendAsync(Encoding.UTF8.GetBytes(answer), WebSocketMessageType.Text, true, cts.Token);
                        break;
                    case "ok":
                        if (status.Next != "challenge" || string.IsNullOrEmpty(status.Token))
                        {
                            throw new Exception($"Bandwidth challenge did not return a receipt: {json}");
                        }

                        _logger.Info($"Bandwidth challenge done in {(DateTime.UtcNow - start).TotalSeconds:N1} seconds");
                        return status.Token;
                    case "fail":
                        throw new Exception($"Bandwidth challenge failed: {status.Message ?? json}");
                }
            }
        }
        catch (OperationCanceledException) when (!_ctx.IsCancellationRequested)
        {
            _logger.Error($"Bandwidth challenge timed out after {timeout.TotalSeconds:N0} seconds");
            throw new TimeoutException($"Bandwidth challenge timed out after {timeout.TotalSeconds:N0} seconds");
        }
    }
}

public class KiwiFlareChallengeModel
{
    public required string Salt { get; set; }
    public required int Difficulty { get; set; }
    /// <summary>sha256 or argon2id</summary>
    public required string Algorithm { get; set; }
}

public class KiwiFlareChallengeSolutionModel
{
    public required string Salt { get; set; }
    public required long Nonce { get; set; }
}

/// <summary>
/// What ttrs answers a submitted solution with. Either success (the clearance cookie comes with it), or the offer of
/// a bandwidth challenge which has to be done first
/// </summary>
public class KiwiFlareAnswerModel
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    /// <summary>bandwidth if that's what has to happen next</summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }
    /// <summary>Token to open the bandwidth WebSocket with</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    [JsonPropertyName("bandwidth_size")]
    public long? BandwidthSize { get; set; }
    [JsonPropertyName("bandwidth_timeout_secs")]
    public long? BandwidthTimeoutSecs { get; set; }
}

/// <summary>
/// The text messages ttrs sends down the bandwidth WebSocket
/// </summary>
public class KiwiFlareBandwidthMessageModel
{
    /// <summary>verify (send the proof now), ok or fail</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("next")]
    public string? Next { get; set; }
    /// <summary>The receipt when status is ok</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
