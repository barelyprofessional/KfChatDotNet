using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KfChatDotNetGui.Models;

namespace KfChatDotNetGui.Helpers;

public static class ForumIdentity
{
    public static async Task<ForumIdentityModel?> GetForumIdentity(string xfSession, Uri sneedChatUri, string? antiDdosPowCookie = null)
    {
        CookieContainer cookies = new CookieContainer();
        cookies.Add(new Cookie("xf_session", xfSession, "/", sneedChatUri.Host));
        if (antiDdosPowCookie != null)
        {
            cookies.Add(new Cookie("z_ddos_pow", antiDdosPowCookie, "/", sneedChatUri.Host));
        }
        using (var client = new HttpClient(new HttpClientHandler {AutomaticDecompression = DecompressionMethods.All, CookieContainer = cookies}))
        {
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("KfChatDotNetGui/1.0");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
            var response = await client.GetAsync(sneedChatUri);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            var accountRegex = new Regex(@"user: (.+),");
            var match = accountRegex.Match(html);
            if (!match.Success)
            {
                throw new Exception("Shitty regex failed to extract account information");
            }

            var accountJs = match.Groups[1].Value;
            return JsonSerializer.Deserialize<ForumIdentityModel>(accountJs);
        }
    }
}