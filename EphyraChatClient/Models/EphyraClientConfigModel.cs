namespace EphyraChatClient.Models;

public class EphyraClientConfigModel
{
    /// <summary>
    /// Public mount root of the chat host before /v1, e.g. https://kiwifarms.st/chat
    /// The WebSocket ends up being wss://kiwifarms.st/chat/v1/connect
    /// </summary>
    public required Uri ChatEndpoint { get; set; }
    /// <summary>
    /// Root of the forum which hands out tickets, e.g. https://kiwifarms.st
    /// </summary>
    public required Uri ForumBaseUri { get; set; }
    /// <summary>
    /// Cookies to start out with. To skip the ticket dance on startup this wants a still valid ephyra_chat_session.
    /// If you're not supplying a <see cref="TicketProvider"/> it also needs the forum cookies (xf_user, xf_session, etc.)
    /// An empty dictionary is fine, it just means the first connection attempt 401s and a ticket gets requested
    /// </summary>
    public Dictionary<string, string> Cookies { get; set; } = new();
    /// <summary>
    /// Domain the cookies get scoped to. Defaults to the host of <see cref="ForumBaseUri"/>
    /// </summary>
    public string? CookieDomain { get; set; }
    public string? Proxy { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>
    /// Origin header sent with the WebSocket upgrade and the session POST, like a browser on the forum would.
    /// Defaults to the origin of <see cref="ForumBaseUri"/>. Set to an empty string to not send one at all
    /// </summary>
    public string? Origin { get; set; }
    /// <summary>
    /// Called whenever the chat host refuses our session and a fresh ticket is needed from the forum.
    /// Return the ticket (v4.public....) or throw. Leave null to have the client ask the forum itself using
    /// <see cref="Cookies"/>, which is fine as long as nothing like KiwiFlare is standing in the way
    /// </summary>
    public Func<CancellationToken, Task<string>>? TicketProvider { get; set; }
    /// <summary>Path of the forum's ticket route relative to <see cref="ForumBaseUri"/></summary>
    public string TicketPath { get; set; } = "/ephyra-chat/ticket";
    /// <summary>Path of the forum's principal lookup relative to <see cref="ForumBaseUri"/></summary>
    public string WhoisPath { get; set; } = "/ephyra-chat/whois.json";
    /// <summary>Name of the CSRF form field the ticket route expects</summary>
    public string CsrfField { get; set; } = "_xfToken";
    /// <summary>How often to ping the chat host. Web client does it every 30 seconds</summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>How long to wait for the pong before the connection is considered dead</summary>
    public TimeSpan PongTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>
    /// How long edits and deletes wait for the chat host to acknowledge them before giving up
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
