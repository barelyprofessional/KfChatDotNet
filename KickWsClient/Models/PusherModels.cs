using System.Text.Json.Serialization;
using KickWsClient.Converters;

namespace KickWsClient.Models;

public class PusherModels
{
    public class BasePusherEventModel
    {
        /// <summary>
        /// Name of the event
        /// </summary>
        [JsonPropertyName("event")]
        public required string Event { get; set; }
        /// <summary>
        /// Stringified JSON payload
        /// </summary>
        [JsonPropertyName("data")]
        [JsonConverter(typeof(StringOrObjectConverter<string>))]
        public required string Data { get; set; }
        /// <summary>
        /// Channel where event originates. Only included events where a channel is applicable
        /// </summary>
        [JsonPropertyName("channel")]
        public string? Channel { get; set; }
    }

    public class BasePusherRequestModel
    {
        /// <summary>
        /// Name of the event
        /// </summary>
        [JsonPropertyName("event")]
        public required string Event { get; set; }
        /// <summary>
        /// Data as object. It's only stringified for responses
        /// </summary>
        [JsonPropertyName("data")]
        public required object Data { get; set; }
    }

    public class PusherConnectionEstablishedEventModel
    {
        /// <summary>
        /// Internal socket ID
        /// </summary>
        [JsonPropertyName("socket_id")]
        public required string SocketId { get; set; }
        /// <summary>
        /// Timeout on no activity in seconds
        /// </summary>
        [JsonPropertyName("activity_timeout")]
        public int ActivityTimeout { get; set; }
    }

    public class PusherSubscribeRequestModel
    {
        /// <summary>
        /// Token to authenticate with, use an empty string for guest.
        /// </summary>
        [JsonPropertyName("auth")]
        public string Auth { get; set; } = "";
        /// <summary>
        /// Channel you wish to subscribe to. 'channel.2515504' for stream events. 'chatrooms.2515504.v2' for chat where 2515504 is the channel ID
        /// </summary>
        [JsonPropertyName("channel")]
        public required string Channel { get; set; }
    }

    public class PusherUnsubscribeRequestModel
    {
        /// <summary>
        /// Channel you wish to unsubscribe from, e.g. 'channel.2515504'
        /// </summary>
        [JsonPropertyName("channel")]
        public required string Channel { get; set; }
    }
}