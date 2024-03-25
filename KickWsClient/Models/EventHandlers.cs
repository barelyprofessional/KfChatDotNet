using Websocket.Client;

namespace KickWsClient.Models;

public class EventHandlers
{
    public delegate void OnPusherConnectionEstablishedEventHandler(object sender,
        PusherModels.PusherConnectionEstablishedEventModel? e);

    public delegate void OnPusherSubscriptionSucceededEventHandler(object sender, PusherModels.BasePusherEventModel e);

    public delegate void OnPusherPongEventHandler(object sender, PusherModels.BasePusherEventModel e);

    public delegate void OnFollowersUpdatedEventHandler(object sender, KickModels.FollowersUpdatedEventModel? e);

    public delegate void OnChatMessageEventHandler(object sender, KickModels.ChatMessageEventModel? e);

    public delegate void OnChannelSubscriptionEventHandler(object sender, KickModels.ChannelSubscriptionEventModel? e);

    public delegate void OnSubscriptionEventHandler(object sender, KickModels.SubscriptionEventModel? e);

    public delegate void OnMessageDeletedEventHandler(object sender, KickModels.MessageDeletedEventModel? e);

    public delegate void OnUserBannedEventHandler(object sender, KickModels.UserBannedEventModel? e);

    public delegate void OnUserUnbannedEventHandler(object sender, KickModels.UserUnbannedEventModel? e);

    public delegate void OnUpdatedLiveStreamEventHandler(object sender, KickModels.UpdatedLiveStreamEventModel? e);

    public delegate void OnStopStreamBroadcastEventHandler(object sender, KickModels.StopStreamBroadcastEventModel? e);

    public delegate void OnStreamerIsLiveEventHandler(object sender, KickModels.StreamerIsLiveEventModel? e);

    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e);

    public delegate void OnWsReconnectEventHandler(object sender, ReconnectionInfo e);

    public delegate void OnWsMessageReceivedEventHandler(object sender, ResponseMessage e);

    public delegate void OnPollUpdateEventHandler(object sender, KickModels.PollUpdateEventModel? e);
}