using System;

namespace KfChatDotNetGui.Models;

public class SettingsModel
{
    public string XfSessionToken { get; set; }
    public Uri WsUri { get; set; }
    public int ReconnectTimeout { get; set; }
    public string AntiDdosPow { get; set; }
    public string Username { get; set; }
}