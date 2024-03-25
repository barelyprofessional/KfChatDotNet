using System;
using ReactiveUI;

namespace KfChatDotNetGui.ViewModels;

public class IdentitySettingsWindowViewModel : ViewModelBase
{
    private Uri _wsUri = new ("wss://kiwifarms.net/chat.ws");

    public Uri WsUri
    {
        get => _wsUri;
        set => this.RaiseAndSetIfChanged(ref _wsUri, value);
    }

    private string _xfSessionToken;

    public string XfSessionToken
    {
        get => _xfSessionToken;
        set => this.RaiseAndSetIfChanged(ref _xfSessionToken, value);
    }
    
    private string _antiDdosPow;

    public string AntiDdosPow
    {
        get => _antiDdosPow;
        set => this.RaiseAndSetIfChanged(ref _antiDdosPow, value);
    }
    
    private string _username;

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    private int _reconnectTimeout = 30;

    public int ReconnectTimeout
    {
        get => _reconnectTimeout;
        set => this.RaiseAndSetIfChanged(ref _reconnectTimeout, value);
    }
}