namespace KfChatDotNetWsClient.Models;

public class ChatClientConfigModel
{
    // XF session token. Sent as a cookie to auth the user
    public string? XfSessionToken { get; set; }
    // Currently wss://kiwifarms.net/chat.ws
    public Uri WsUri { get; set; }
    public int ReconnectTimeout { get; set; } = 30;
    public string CookieDomain { get; set; } = "kiwifarms.net";
    public string? Proxy { get; set; }
}