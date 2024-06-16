using Websocket.Client;

namespace ThreeXplWsClient.Events;

public class EventHandlers
{
    public delegate void OnThreeXplPing(object sender, int connectionId);

    public delegate void OnThreeXplPush(object sender, ThreeXplPushModel e, int connectionId);
    
    public delegate void OnWsDisconnectionEventHandler(object sender, DisconnectionInfo e, int connectionId);

    public delegate void OnWsReconnectEventHandler(object sender, ReconnectionInfo e, int connectionId);

    public delegate void OnWsMessageReceivedEventHandler(object sender, ResponseMessage e, int connectionId);

    public delegate void OnThreeXplConnect(object sender, ThreeXplConnectDataModel e, int connectionId);

    public delegate void OnThreeXplError(object sender, ThreeXplErrorModel e, int connectionId);

    public delegate void OnThreeXplSubscribe(object sender, ThreeXplSubscribeModel e, int connectionId);
}