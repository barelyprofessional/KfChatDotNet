namespace KfChatDotNetWsClient.Models;

public class ChatClientConfigModel
{
    public required Dictionary<string, string> Cookies { get; set; } = new();
    // Currently wss://kiwifarms.net/chat.ws
    public required Uri WsUri { get; set; }
    public int ReconnectTimeout { get; set; } = 30;
    public string CookieDomain { get; set; } = "kiwifarms.net";
    public string? Proxy { get; set; }
}