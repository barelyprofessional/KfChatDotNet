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
        using var client = new HttpClient(GetHttpClientHandler());
        var response = await client.GetAsync($"https://{kfDomain}/", _ctx);
        var document = new HtmlDocument();
        document.Load(await response.Content.ReadAsStreamAsync(_ctx));
        var challengeData = document.DocumentNode.SelectSingleNode("//html[@id=\"sssg\"]");
        if (challengeData == null)
        {
            _logger.Info("challengeData was null. Couldn't find html element with id = sssg, returning null");
            return null;
        }

        if (!challengeData.Attributes.Contains("data-sssg-challenge")) throw new Exception("data-sssg-challenge attribute missing");
        if (!challengeData.Attributes.Contains("data-sssg-difficulty")) throw new Exception("data-sssg-difficulty attribute missing");
        if (!challengeData.Attributes.Contains("data-sssg-patience")) throw new Exception("data-sssg-patience attribute missing");
        var salt = challengeData.Attributes["data-sssg-challenge"].Value;
        var difficulty = Convert.ToInt32(challengeData.Attributes["data-sssg-difficulty"].Value);
        var patience = TimeSpan.FromMinutes(Convert.ToDouble(challengeData.Attributes["data-sssg-patience"].Value));
        _logger.Info($"Got sssg challenge parameters. Salt = {salt}, Difficulty = {difficulty}, Patience = {patience.TotalMinutes} minutes");
        return new KiwiFlareChallengeModel
        {
            Salt = salt,
            Difficulty = difficulty,
            Patience = patience
        };
    }

    // A hash is considered solved if the first number of bits (as defined by difficulty) are all zero
    private bool TestHash(byte[] hash, int difficulty)
    {
        var bitArray = new BitArray(hash);
        var i = 0;
        // BitArrays can't be sliced so just step through it
        while (i < difficulty)
        {
            // If any of the bits are set to 1, it's not a valid hash
            if (bitArray[i])
            {
                return false;
            }
            i++;
        }
        // If we got this far, means they were all zero!
        return true;
    }

    private Task<KiwiFlareChallengeSolutionModel> ChallengeWorker(KiwiFlareChallengeModel challenge)
    {
        var nonce = _random.NextInt64();
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
    
    public async Task<bool> CheckAuth(string authToken)
    {
        using var client = new HttpClient(GetHttpClientHandler());
        var formData = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("f", authToken),
        });

        var response = await client.PostAsync($"https://{kfDomain}/.sssg/api/check", formData, _ctx);
        // KiwiFlare has been turned off
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_ctx);
        if (json.TryGetProperty("error", out var error))
        {
            _logger.Error($"Received error when checking the auth token: {error.GetString()}");
            return false;
        }

        if (json.TryGetProperty("auth", out _))
        {
            return true;
        }

        _logger.Error("Auth property was missing from sssg response");
        _logger.Error(json.GetRawText());
        return false;
    }
}

public class KiwiFlareChallengeModel
{
    public required string Salt { get; set; }
    public required int Difficulty { get; set; }
    public required TimeSpan Patience { get; set; }
}

public class KiwiFlareChallengeSolutionModel
{
    public required string Salt { get; set; }
    public required long Nonce { get; set; }
}