using System.Net;
using System.Text.Json;
using EphyraChatClient.Models.Events;
using HtmlAgilityPack;

namespace EphyraChatClient;

/// <summary>
/// The forum side of Ephyra: getting a ticket to hand to the chat host and looking up who a principal is.
/// Bring your own HttpClient with the forum cookies loaded (and whatever else is needed to get past the front door)
/// </summary>
public class EphyraForumClient(HttpClient httpClient, Uri forumBaseUri)
{
    public string TicketPath { get; set; } = "/ephyra-chat/ticket";
    public string WhoisPath { get; set; } = "/ephyra-chat/whois.json";
    public string CsrfField { get; set; } = "_xfToken";

    /// <summary>
    /// Scrape the CSRF token (data-csrf on the html element) off a forum page
    /// </summary>
    /// <param name="path">Any page that renders the regular XenForo template</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<string> GetCsrfTokenAsync(string path = "/", CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(new Uri(forumBaseUri, path), ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return ExtractCsrfToken(content) ??
               throw new EphyraTicketException(ChatAdmissionFailure.ForumUnreachable, (int)response.StatusCode,
                   "Couldn't find data-csrf on the page the forum returned");
    }

    public static string? ExtractCsrfToken(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var csrf = document.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("data-csrf", string.Empty);
        return string.IsNullOrEmpty(csrf) ? null : HtmlEntity.DeEntitize(csrf);
    }

    /// <summary>
    /// Ask the forum for a ticket that the chat host will swap for a session
    /// </summary>
    /// <param name="csrfToken">A CSRF token belonging to the session the cookies are for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The ticket (v4.public....). They're only good for a couple of minutes so use it right away</returns>
    /// <exception cref="EphyraTicketException">Forum said no or didn't answer with a ticket</exception>
    public async Task<string> RequestTicketAsync(string csrfToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(forumBaseUri, TicketPath));
        request.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>(CsrfField, csrfToken)]);
        request.Headers.Accept.ParseAdd("application/json");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new EphyraTicketException(ChatAdmissionFailure.ForumUnreachable, null, "Ticket request failed", e);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Only a JSON object is the forum's own refusal. Anything else (a challenge page, an empty body) is
                // whatever sits in front of the forum and says nothing about whether we're allowed in
                var forumRefusal = response.Content.Headers.ContentType?.MediaType?.Contains("json") == true &&
                                   TryParseObject(body) != null;
                throw new EphyraTicketException(
                    forumRefusal ? ChatAdmissionFailure.ForumRefused : ChatAdmissionFailure.ForumUnreachable, status,
                    $"Forum refused the ticket request with {status}: {Truncate(body)}");
            }

            // A 203 is KiwiFlare wanting a challenge solved, not a ticket
            if (status is < 200 or >= 300 || response.StatusCode == HttpStatusCode.NonAuthoritativeInformation)
            {
                throw new EphyraTicketException(ChatAdmissionFailure.ForumUnreachable, status,
                    $"Forum returned {status} to the ticket request");
            }

            var json = TryParseObject(body);
            if (json == null || !json.Value.TryGetProperty("ticket", out var ticket) ||
                ticket.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(ticket.GetString()))
            {
                throw new EphyraTicketException(ChatAdmissionFailure.ForumUnreachable, status,
                    $"Forum's answer didn't contain a ticket: {Truncate(body)}");
            }

            return ticket.GetString()!;
        }
    }

    /// <summary>
    /// Look up the forum user behind a principal UUID
    /// </summary>
    /// <returns>Null if the forum doesn't know the principal</returns>
    public async Task<ChatWhoisModel?> WhoisAsync(string principalUuid, CancellationToken ct = default)
    {
        var uri = new Uri(forumBaseUri, $"{WhoisPath}?uuid={Uri.EscapeDataString(principalUuid)}");
        using var response = await httpClient.GetAsync(uri, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Whois returned {(int)response.StatusCode}: {Truncate(body)}", null, response.StatusCode);
        }
        return ParseWhois(body).FirstOrDefault(result =>
            result.PrincipalUuid.Equals(principalUuid, StringComparison.OrdinalIgnoreCase));
    }

    // [{"019e618a-6b6f-77fe-94f1-8d5312c9a21d":{"user_id":1,"username":"Null"}}]
    public static List<ChatWhoisModel> ParseWhois(string json)
    {
        var results = new List<ChatWhoisModel>();
        using var document = JsonDocument.Parse(json);
        // It's an array of objects today, but a bare object keyed by UUID would be an obvious thing for it to become
        var containers = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];
        foreach (var container in containers)
        {
            if (container.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in container.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                if (!property.Value.TryGetProperty("user_id", out var userId)) continue;
                int id;
                if (userId.ValueKind == JsonValueKind.Number && userId.TryGetInt32(out var number)) id = number;
                else if (userId.ValueKind == JsonValueKind.String && int.TryParse(userId.GetString(), out var parsed)) id = parsed;
                else continue;
                results.Add(new ChatWhoisModel
                {
                    PrincipalUuid = property.Name,
                    UserId = id,
                    Username = property.Value.TryGetProperty("username", out var username) &&
                               username.ValueKind == JsonValueKind.String
                        ? username.GetString()
                        : null
                });
            }
        }

        return results;
    }

    private static JsonElement? TryParseObject(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";
}

public class EphyraTicketException(ChatAdmissionFailure failure, int? statusCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ChatAdmissionFailure Failure { get; } = failure;
    public int? StatusCode { get; } = statusCode;
}
