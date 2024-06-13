namespace KfChatDotNetKickBot;

public class Models
{
    public class ConfigModel
    {
        public Uri PusherEndpoint { get; set; } =
            new("wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=7.6.0&flash=false");

        public Uri KfWsEndpoint { get; set; } = new("wss://kiwifarms.st:9443/chat.ws");

        public List<string> PusherChannels { get; set; } = [];
        public int KfChatRoomId { get; set; }
        // Proxy to use for connecting to Sneedchat
        public string? KfProxy { get; set; }
        // Proxy to use for the Pusher websocket
        // e.g. socks5://blahblah:1080
        public string? PusherProxy { get; set; }
        public int KfReconnectTimeout { get; set; } = 30;
        public int PusherReconnectTimeout { get; set; } = 30;
        public bool EnableGambaSeshDetect { get; set; } = true;
        public int GambaSeshUserId { get; set; } = 168162;
        public string KickIcon { get; set; } = "https://i.ibb.co/0cqwscx/kick.png";
        public string KfDomain { get; set; } = "kiwifarms.st";
        public required string KfUsername { get; set; }
        public required string KfPassword { get; set; }
        public string ChromiumPath { get; set; } = "chromium_install";
    }
}