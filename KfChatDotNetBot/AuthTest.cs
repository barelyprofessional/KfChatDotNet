using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using NLog;

namespace KfChatDotNetBot;

/// <summary>
/// Auth-only smoke test. Clears the Tartarus gate (proof of work + Spur Monocle assessment) and logs into the
/// forum, then exits. No chat socket, no room join, no services, so it neither reads nor responds to anything.
///
/// Run it with: dotnet run --project KfChatDotNetBot -- --auth-test [--fresh]
///
/// --fresh wipes the cached cookies first, which is what you want when testing the gate itself. Without it a
/// still-valid clearance means the challenge path never runs and the test proves nothing about it.
/// </summary>
public static class AuthTest
{
    public static async Task<int> Run(bool fresh)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsDomain, BuiltIn.Keys.KiwiFarmsProxy, BuiltIn.Keys.KiwiFarmsUsername,
            BuiltIn.Keys.KiwiFarmsPassword
        ]);
        var domain = settings[BuiltIn.Keys.KiwiFarmsDomain].Value;
        var username = settings[BuiltIn.Keys.KiwiFarmsUsername].Value;
        var password = settings[BuiltIn.Keys.KiwiFarmsPassword].Value;
        if (domain == null)
        {
            logger.Error($"{BuiltIn.Keys.KiwiFarmsDomain} is not set");
            return 1;
        }

        Console.WriteLine($"[auth-test] domain    : {domain}");
        Console.WriteLine($"[auth-test] proxy     : {settings[BuiltIn.Keys.KiwiFarmsProxy].Value ?? "(none)"}");
        Console.WriteLine($"[auth-test] username  : {username ?? "(not set, gate only)"}");

        var tokenService = new KfTokenService(domain, settings[BuiltIn.Keys.KiwiFarmsProxy].Value);
        if (fresh)
        {
            Console.WriteLine("[auth-test] wiping cached cookies so the gate actually gets exercised");
            tokenService.WipeCookies();
        }

        try
        {
            // IsLoggedIn loads /login, which is what trips the 203 and drives the whole gate flow
            var loggedIn = await tokenService.IsLoggedIn();
            Console.WriteLine($"[auth-test] cleared the gate, logged in = {loggedIn}");
            if (!loggedIn)
            {
                if (username == null || password == null)
                {
                    Console.WriteLine("[auth-test] no credentials set, stopping after the gate");
                    return 0;
                }
                Console.WriteLine("[auth-test] logging in");
                await tokenService.PerformLogin(username, password);
                loggedIn = await tokenService.IsLoggedIn();
                Console.WriteLine($"[auth-test] logged in = {loggedIn}");
                if (!loggedIn)
                {
                    Console.WriteLine("[auth-test] FAILED, the login did not stick");
                    return 1;
                }
            }

            // Names and lengths only. These are bearer tokens for the account, they don't belong on a terminal
            foreach (var cookie in tokenService.GetCookies())
            {
                Console.WriteLine($"[auth-test] cookie    : {cookie.Key} ({cookie.Value.Length} chars)");
            }
            Console.WriteLine("[auth-test] OK");
            return 0;
        }
        catch (TtrsChallengeRejectedException e) when (e.IsConnectionRejection)
        {
            Console.WriteLine("[auth-test] FAILED: Spur rejected this connection (MCL). This address reads as a " +
                              $"VPN, proxy or datacentre. Set {BuiltIn.Keys.MonocleProxy} to mint from somewhere " +
                              "acceptable, or move the bot.");
            return 2;
        }
        catch (TtrsChallengeRejectedException e)
        {
            Console.WriteLine($"[auth-test] FAILED: the gate rejected the solution, reason = {e.Reason}");
            return 2;
        }
        catch (MonocleAssessmentUnavailableException e)
        {
            Console.WriteLine($"[auth-test] FAILED: could not mint a Monocle assessment. {e.Message}");
            return 3;
        }
        catch (UnsupportedPowAlgorithmException e)
        {
            Console.WriteLine($"[auth-test] FAILED: {e.Message}");
            return 4;
        }
        catch (KfTokenService.KiwiFarmsLogonFailedException)
        {
            Console.WriteLine("[auth-test] FAILED: the gate was cleared but the forum refused the credentials. " +
                              "Check the log above for the error the page gave.");
            return 5;
        }
        catch (Exception e)
        {
            logger.Error(e);
            Console.WriteLine($"[auth-test] FAILED: {e.Message}");
            return 1;
        }
    }
}
