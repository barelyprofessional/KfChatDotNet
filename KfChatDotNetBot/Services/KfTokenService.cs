using System.Net;
using System.Text.Json;
using HtmlAgilityPack;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot.Services;

public class KfTokenService
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CancellationToken _ctx;
    private CookieContainer _cookies = new();
    private KiwiFlare _kiwiFlare;
    private readonly string _kfDomain;
    private readonly string? _proxy;

    public KfTokenService(string kfDomain, string? proxy = null, CancellationToken? cancellationToken = null)
    {
        _ctx = cancellationToken ?? CancellationToken.None;
        _kiwiFlare = new KiwiFlare(kfDomain, proxy, cancellationToken);
        _proxy = proxy;
        _kfDomain = kfDomain;
        var cachedCookies = Helpers.GetValue(BuiltIn.Keys.KiwiFarmsCookies).Result
            .JsonDeserialize<Dictionary<string, string>>();
        // This shouldn't happen as the setting's default value is {}, but I'm just doing it to shut the IDE up
        if (cachedCookies == null) return;
        foreach (var key in cachedCookies.Keys)
        {
            _cookies.Add(new Cookie(key, cachedCookies[key], "/", _kfDomain));
        }
    }
    
    private HttpClientHandler GetHttpClientHandler()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (_proxy != null)
        {
            handler.Proxy = new WebProxy(_proxy);
            handler.UseProxy = true;
        }
        handler.CookieContainer = _cookies;
        return handler;
    }

    private async Task CheckClearanceToken()
    {
        var clearanceCookie = _cookies.GetAllCookies()["sssg_clearance"];
        _logger.Debug($"Got clearance cookie with value: {clearanceCookie}");
        if (clearanceCookie != null)
        {
            if (await _kiwiFlare.CheckAuth(clearanceCookie.Value)) return;
            _logger.Debug("Cookie is no longer valid, removing");
            _cookies.GetAllCookies().Remove(clearanceCookie);
        }
        _logger.Debug("Getting a new clearance token");
        var i = 0;
        // Shitty retry logic as the forum is still annoyingly unstable
        while (i < 10)
        {
            i++;
            try
            {
                var challenge = await _kiwiFlare.GetChallenge();
                var solution = await _kiwiFlare.SolveChallenge(challenge);
                var token = await _kiwiFlare.SubmitAnswer(solution);
                _cookies.Add(new Cookie("sssg_clearance", token, "/", _kfDomain));
                _logger.Debug("Successfully retrieved a new token and added to the cookie container");
                return;
            }
            catch (Exception e)
            {
                _logger.Error($"Failed to solve the KiwiFlare challenge, attempt {i} of 10");
                _logger.Error(e);
            }
        }
        _logger.Error("Ran out of attempts");
        throw new Exception("Failed to solve the challenge");
    }

    private async Task<Stream> GetLoginPage()
    {
        _logger.Debug("Checking clearance token is actually valid first");
        await CheckClearanceToken();
        using var client = new HttpClient(GetHttpClientHandler());
        var response = await client.GetAsync($"https://{_kfDomain}/login", _ctx);
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
        {
            _logger.Error("Caught a 203 response when trying to load logon page which means we were KiwiFlare challenged");
            throw new KiwiFlareChallengedException();
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(_ctx);
    }

    public async Task<bool> IsLoggedIn()
    {
        var document = new HtmlDocument();
        document.Load(await GetLoginPage());
        var html = document.DocumentNode.SelectSingleNode("//html");
        if (html == null) throw new Exception("Caught a null when retrieving html element");
        if (!html.Attributes.Contains("data-logged-in"))
        {
            throw new Exception("data-logged-in attribute missing");
        }

        return html.Attributes["data-logged-in"].Value == "true";
    }

    public async Task PerformLogin(string username, string password)
    {
        var document = new HtmlDocument();
        document.Load(await GetLoginPage());
        var html = document.DocumentNode.SelectSingleNode("//html");
        if (html == null) throw new Exception("Caught a null when retrieving html element");
        // Already logged in
        if (html.GetAttributeValue("data-logged-in", "false") == "true") return;
        if (!html.Attributes.Contains("data-csrf")) throw new Exception("data-csrf missing from html element");
        var csrf = html.GetAttributeValue("data-csrf", string.Empty);
        var formData = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("_xfToken", csrf),
            new("login", username),
            new("password", password),
            new("_xfRedirect", $"https://{_kfDomain}/"),
            new("remember", "1")
        });
        using var client = new HttpClient(GetHttpClientHandler());
        var postResponse = await client.PostAsync($"https://{_kfDomain}/login/login", formData, _ctx);
        if (postResponse.StatusCode == HttpStatusCode.SeeOther)
        {
            _logger.Debug("Got HTTP response 303. Success!");
            return;
        }
        postResponse.EnsureSuccessStatusCode();
        _logger.Info($"Received HTTP response {postResponse.StatusCode}, checking to see if we're logged in");
        var postDocument = new HtmlDocument();
        postDocument.Load(await postResponse.Content.ReadAsStreamAsync(_ctx));
        html = postDocument.DocumentNode.SelectSingleNode("//html");
        if (html == null) throw new Exception("Caught a null when retrieving html element");
        // Logged in!
        if (html.GetAttributeValue("data-logged-in", "false") == "true") return;
        _logger.Error("Not logged in :(");
        var message =
            postDocument.DocumentNode.SelectSingleNode(
                "//div[@class=\"blockMessage blockMessage--error blockMessage--iconic\"]");
        _logger.Error($"Error from the page was {message?.InnerText}");
        throw new KiwiFarmsLogonFailedException();
    }

    public string? GetXfSessionCookie()
    {
        _logger.Debug("JSON serialization of all the cookies");
        _logger.Debug(JsonSerializer.Serialize(_cookies.GetAllCookies()));
        var cookie = _cookies.GetAllCookies()["xf_session"];
        _logger.Debug($"xf_session => {cookie?.Value}");
        return cookie?.Value;
    }

    public async Task SaveCookies()
    {
        _logger.Debug("Saving cookies");
        var cookiesToSave = _cookies.GetAllCookies().ToDictionary(cookie => cookie.Name, cookie => cookie.Value);
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.KiwiFarmsCookies, cookiesToSave);
    }

    public void WipeCookies()
    {
        _logger.Info("Wiping out cookies");
        _cookies = new CookieContainer();
    }
    
    public class KiwiFlareChallengedException : Exception;
    public class KiwiFarmsLogonFailedException : Exception;
}