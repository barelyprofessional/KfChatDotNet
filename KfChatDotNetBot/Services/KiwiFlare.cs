using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using NLog;

namespace KfChatDotNetBot.Services;

public class KiwiFlare(string kfDomain, string? proxy = null, CancellationToken? cancellationToken = null)
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private string? _proxy = proxy;
    private CancellationToken _ctx = cancellationToken ?? CancellationToken.None;
    private string _kfDomain = kfDomain;
    private Random _random = new Random();

    private HttpClientHandler GetHttpClientHandler()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.Proxy = new WebProxy(_proxy);
            handler.UseProxy = true;
        }

        return handler;
    }

    public async Task<KiwiFlareChallengeModel> GetChallenge()
    {
        using var client = new HttpClient(GetHttpClientHandler());
        var response = await client.GetAsync($"https://{_kfDomain}/", _ctx);
        var document = new HtmlDocument();
        document.Load(await response.Content.ReadAsStreamAsync(_ctx));
        var challengeData = document.DocumentNode.SelectSingleNode("//html[@id=\"sssg\"]");
        if (challengeData == null)
        {
            throw new Exception("challengeData was null. Couldn't find html element with id = sssg");
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

    private async Task<KiwiFlareChallengeSolutionModel> ChallengeWorker(KiwiFlareChallengeModel challenge)
    {
        var found = false;
        var nonce = _random.NextInt64();
        while (!found)
        {
            // Incrementing nonce first as then I can just pass TestHash straight into found and reuse the value
            nonce++;
            var input = Encoding.UTF8.GetBytes($"{challenge.Salt}{nonce}");
            found = TestHash(SHA256.HashData(input), challenge.Difficulty);
        }

        return new KiwiFlareChallengeSolutionModel
        {
            Nonce = nonce,
            Salt = challenge.Salt
        };
    }

    public async Task<KiwiFlareChallengeSolutionModel> SolveChallenge(KiwiFlareChallengeModel challenge)
    {
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

        var response = await client.PostAsync($"https://{_kfDomain}/.sssg/api/answer", formData, _ctx);
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

        var response = await client.PostAsync($"https://{_kfDomain}/.sssg/api/check", formData, _ctx);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_ctx);
        if (json.TryGetProperty("error", out var error))
        {
            _logger.Error($"Received error when checking the auth token: {error.GetString()}");
            return false;
        }

        if (json.TryGetProperty("auth", out var auth))
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