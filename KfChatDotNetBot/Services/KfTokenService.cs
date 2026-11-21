using System.Net;
using System.Text.Json;
using EphyraChatClient;
using EphyraChatClient.Models.Events;
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
        var cachedCookies = SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsCookies).Result
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

    private async Task GetNewClearanceToken()
    {
        _logger.Debug("Getting a new clearance token");
        var i = 0;
        // Shitty retry logic as the forum is still annoyingly unstable
        while (i < 10)
        {
            i++;
            try
            {
                var challenge = await _kiwiFlare.GetChallenge();
                if (challenge == null)
                {
                    _logger.Error("Challenge data was null. Might be KiwiFlare is only partially enabled? Not going to do anything.");
                    return;
                }
                var solution = await _kiwiFlare.SolveChallenge(challenge);
                string token;
                if (challenge.IsTtrs)
                {
                    token = await _kiwiFlare.SubmitAnswerTtrs(solution);
                    _cookies.Add(new Cookie("ttrs_clearance", token, "/", _kfDomain));

                }
                else
                {
                    token = await _kiwiFlare.SubmitAnswer(solution);
                    _cookies.Add(new Cookie("sssg_clearance", token, "/", _kfDomain));
                }
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

    /// <summary>
    /// Solve the KiwiFlare challenge and put the clearance cookie it earns in the jar. For when something outside this
    /// class got a 203, such as the chat's WebSocket
    /// </summary>
    public async Task SolveChallengeAsync()
    {
        await GetNewClearanceToken();
    }

    private async Task<string> GetLoginPage()
    {
        using var client = new HttpClient(GetHttpClientHandler());
        var response = await client.GetAsync($"https://{_kfDomain}/login", _ctx);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(_ctx);
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
        {
            _logger.Info("Caught a 203 response when trying to load the logon page which means we have to solve a KiwiFlare challenge");
            await GetNewClearanceToken();
            _logger.Info("Solved the challenge, now going to grab the login page again");
            response = await client.GetAsync($"https://{_kfDomain}/login", _ctx);
            content = await response.Content.ReadAsStringAsync(_ctx);
        }
        return content;
    }

    public async Task<bool> IsLoggedIn()
    {
        var document = new HtmlDocument();
        document.LoadHtml(await GetLoginPage());
        var html = document.DocumentNode.SelectSingleNode("//html");
        if (html == null) throw new Exception("Caught a null when retrieving html element");
        if (!html.Attributes.Contains("data-logged-in"))
        {
            throw new Exception("data-logged-in attribute missing");
        }

        var success = html.Attributes["data-logged-in"].Value == "true";
        if (success)
        {
            await SaveCookies();
        }

        return success;
    }

    public async Task PerformLogin(string username, string password)
    {
        var document = new HtmlDocument();
        document.LoadHtml(await GetLoginPage());
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

    public Dictionary<string, string> GetCookies()
    {
        return _cookies.GetAllCookies().ToDictionary(cookie => cookie.Name, cookie => cookie.Value);
    }

    public async Task SaveCookies()
    {
        _logger.Debug("Saving cookies");
        var cookiesToSave = _cookies.GetAllCookies().ToDictionary(cookie => cookie.Name, cookie => cookie.Value);
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KiwiFarmsCookies, cookiesToSave);
    }

    public void WipeCookies()
    {
        _logger.Info("Wiping out cookies");
        _cookies = new CookieContainer();
    }

    /// <summary>
    /// Add (or replace) a cookie in the jar. Used for holding onto the chat session cookie Ephyra issues so it gets
    /// persisted along with the forum ones
    /// </summary>
    public void SetCookie(string name, string value)
    {
        _cookies.Add(new Cookie(name, value, "/", _kfDomain));
    }

    private EphyraForumClient GetForumClient(HttpClient client)
    {
        return new EphyraForumClient(client, new Uri($"https://{_kfDomain}"));
    }

    /// <summary>
    /// Get a ticket from the forum which the Ephyra chat host will swap for a session. Logs in first if needed
    /// </summary>
    /// <param name="username">Username to log in with if the session turns out to be dead</param>
    /// <param name="password">Password to log in with if the session turns out to be dead</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The ticket. Only good for a couple of minutes</returns>
    public async Task<string> GetChatTicketAsync(string? username, string? password, CancellationToken ct = default)
    {
        // GetLoginPage deals with KiwiFlare, so by the time it's back we should have clearance for the POST as well
        var html = await GetLoginPage();
        if (!IsLoggedInHtml(html))
        {
            if (username == null || password == null)
            {
                throw new KiwiFarmsLogonFailedException();
            }

            _logger.Info("Not logged into the forum, logging in before asking for a chat ticket");
            await PerformLogin(username, password);
            html = await GetLoginPage();
        }

        var csrf = EphyraForumClient.ExtractCsrfToken(html);
        if (csrf == null) throw new Exception("data-csrf missing from html element");
        using var client = new HttpClient(GetHttpClientHandler());
        return await GetForumClient(client).RequestTicketAsync(csrf, ct);
    }

    private static bool IsLoggedInHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("data-logged-in", "false") == "true";
    }

    /// <summary>
    /// Ask the forum which user is behind an Ephyra principal UUID
    /// </summary>
    /// <returns>Null if the forum doesn't know the principal</returns>
    public async Task<ChatWhoisModel?> WhoisAsync(string principalUuid, CancellationToken ct = default)
    {
        using var client = new HttpClient(GetHttpClientHandler());
        try
        {
            return await GetForumClient(client).WhoisAsync(principalUuid, ct);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
        {
            _logger.Info("Caught a 203 response when doing a whois which means we have to solve a KiwiFlare challenge");
            await GetNewClearanceToken();
            return await GetForumClient(client).WhoisAsync(principalUuid, ct);
        }
    }
    
    public class KiwiFarmsLogonFailedException : Exception;
}