using System.Net;
using System.Text.Json;
using NLog;
using PuppeteerSharp;

namespace KfChatDotNetBot.Services;

public class KfTokenService
{
    // Shout out Gamba Sesh for open sourcing his token retriever which heavily inspired this implementation
    public static async Task<string> FetchSessionTokenAsync(string domain, string username, string password, string browserPath, string? proxy = null)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
            { Browser = SupportedBrowser.Chromium, Path = browserPath });
        if (proxy != null)
        {
            browserFetcher.WebProxy = new WebProxy(proxy);
            logger.Debug($"Detected proxy settings for browser download: {proxy}");
        }

        var installedBrowser = await browserFetcher.DownloadAsync();
        logger.Debug("Downloaded browser");
        List<string> launchArgs = ["--no-sandbox"];
        if (proxy != null)
        {
            logger.Debug($"Configuring Chromium to use proxy {proxy}");
            launchArgs.Add($"--proxy-server=\"{proxy}\"");
        }

        var launchOptions = new LaunchOptions
        {
            Headless = false, ExecutablePath = installedBrowser.GetExecutablePath(), UserDataDir = "kf_profile",
            Args = launchArgs.ToArray()
        };

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();
        await page.GoToAsync($"https://{domain}/login");
        await page.WaitForSelectorAsync("img[alt=\"Kiwi Farms\"]");
        if (await page.QuerySelectorAsync("html[data-template=\"login\"]") == null)
        {
            logger.Debug("Page template is not login. This is expected if we're already logged in. Reloading page to get the freshest cookies then retrieving");
            await page.ReloadAsync();
            return await GetXfSessionCookie();
        }
        
        var usernameFieldSelector = await page.QuerySelectorAsync("input[autocomplete=\"username\"]");
        var passwordFieldSelector = await page.QuerySelectorAsync("input[autocomplete=\"current-password\"]");
        var loginButtonSelector = await page.QuerySelectorAsync("div[class=\"formSubmitRow-controls\"] > button[type=\"submit\"]");
        if (usernameFieldSelector == null || passwordFieldSelector == null || loginButtonSelector == null)
        {
            // Realistically this shouldn't happen unless Null changes the login template in a big way
            logger.Error("Username/password fields could not be found");
            throw new MissingLoginElementsException();
        }

        await usernameFieldSelector.TypeAsync(username);
        await passwordFieldSelector.TypeAsync(password);
        await loginButtonSelector.ClickAsync();
        logger.Debug("Login fields have been filled out and button clicked. Awaiting page navigation.");
        await page.WaitForNavigationAsync();
        logger.Debug("Navigation completed. Doing the cookie needful");
        return await GetXfSessionCookie();
        
        async Task<string> GetXfSessionCookie()
        {
            var cookies = await page.GetCookiesAsync();
            var xfSession = cookies.FirstOrDefault(x => x.Name == "xf_session");
            if (xfSession == null)
            {
                logger.Error("xf_session cookie not set. Cookie data follows");
                logger.Error(JsonSerializer.Serialize(cookies));
                throw new MissingSessionCookieException();
            }
            logger.Debug($"Returning xf_session value: {xfSession.Value}");
            return xfSession.Value;
        }
    }

    public class MissingSessionCookieException : Exception;
    public class MissingLoginElementsException : Exception;
}