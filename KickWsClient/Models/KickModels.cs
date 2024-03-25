using Newtonsoft.Json;

namespace KickWsClient.Models;

public class KickModels
{
    public class ChatMessageSenderIdentityBadgeModel
    {
        /// <summary>
        /// Internal type for badge e.g. moderator
        /// </summary>
        [JsonProperty("type")]
        public required string Type { get; set; }
        /// <summary>
        /// Friendly name for badge e.g. Moderator
        /// </summary>
        [JsonProperty("text")]
        public required string Text { get; set; }
        /// <summary>
        /// Count (if applicable) for badge (e.g. sub count for gifted subs)
        /// </summary>
        [JsonProperty("count")]
        public int? Count { get; set; }
    }
    
    public class ChatMessageSenderIdentityModel
    {
        /// <summary>
        /// User's hex color
        /// </summary>
        [JsonProperty("color")]
        public required string Color { get; set; }

        /// <summary>
        /// Badges a user has
        /// </summary>
        [JsonProperty("badges")]
        public List<ChatMessageSenderIdentityBadgeModel> Badges = [];
    }
    
    public class ChatMessageSenderModel
    {
        /// <summary>
        /// Kick internal user ID
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Kick display name
        /// </summary>
        [JsonProperty("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Kick slug (for URLs)
        /// </summary>
        [JsonProperty("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Identity info for display color and badges
        /// </summary>
        [JsonProperty("identity")]
        public required ChatMessageSenderIdentityModel Identity { get; set; }
    }

    public class ChatMessageMetadataOriginalSenderModel
    {
        /// <summary>
        /// Original sender's user ID
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Original sender's username
        /// </summary>
        [JsonProperty("username")]
        public required string Username { get; set; }
    }

    public class ChatMessageMetadataOriginalMessageModel
    {
        /// <summary>
        /// ID (GUID) of the original message
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Content of the original message
        /// </summary>
        [JsonProperty("content")]
        public required string Content { get; set; }
    }

    public class ChatMessageMetadataModel
    {
        /// <summary>
        /// Sender of the message that this message is in reply to
        /// </summary>
        [JsonProperty("original_sender")]
        public required ChatMessageMetadataOriginalSenderModel OriginalSender { get; set; }
        /// <summary>
        /// Content of the message that this message is in reply to
        /// </summary>
        [JsonProperty("original_message")]
        public required ChatMessageMetadataOriginalMessageModel OriginalMessage { get; set; }
    }
    
    public class FollowersUpdatedEventModel
    {
        /// <summary>
        /// Channel follower count
        /// </summary>
        [JsonProperty("followersCount")]
        public int FollowersCount { get; set; }
        /// <summary>
        /// ID to identify what chatroom this event belongs to
        /// </summary>
        [JsonProperty("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Maybe returns your username if you're auth'd? No idea. Just returned null for me
        /// </summary>
        [JsonProperty("username")]
        public string? Username { get; set; }
        /// <summary>
        /// Epoch value that signifies ???
        /// </summary>
        [JsonProperty("created_at")]
        public int? CreatedAtEpoch { get; set; }
        // It returned true even though I'm not signed in which makes no sense, so I'll assume there's a chance it'll
        // suddenly appear and mark as nullable as it's not really a useful property anyway.
        /// <summary>
        /// Does it mean we're following? Who knows, returns true even if you're a guest
        /// </summary>
        [JsonProperty("followed")]
        public bool? Followed { get; set; }
    }

    public class ChatMessageEventModel
    {
        /// <summary>
        /// Message unique GUID that's referenced for replies and deletions
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Chatroom ID you can use to differentiate this from other rooms if you sub to multiple at a time
        /// </summary>
        [JsonProperty("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Content of the message. Emotes are encoded like [emote:161238:russW] which translates to -> https://files.kick.com/emotes/161238/fullsize
        /// </summary>
        [JsonProperty("content")]
        public required string Content { get; set; }
        /// <summary>
        /// Regular message is 'message', replies are 'reply'
        /// </summary>
        [JsonProperty("type")]
        public required string Type { get; set; }
        // Why created at is an epoch for followers updated but ISO8601 for chat messages is just a mystery
        /// <summary>
        /// Time message was sent
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>
        /// Sender of the message
        /// </summary>
        [JsonProperty("sender")]
        public required ChatMessageSenderModel Sender { get; set; }
        /// <summary>
        /// Message metadata which is set for replies only
        /// </summary>
        [JsonProperty("metadata")]
        public ChatMessageMetadataModel? Metadata { get; set; }
    }

    public class ChannelSubscriptionEventModel
    {
        /// <summary>
        /// User IDs of subscription recipients
        /// </summary>
        [JsonProperty("user_ids")]
        public List<int> UserIds { get; set; } = [];
        /// <summary>
        /// Username of the person who subbed / gifted
        /// </summary>
        [JsonProperty("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Channel ID where the sub event occurred
        /// </summary>
        [JsonProperty("channel_id")]
        public int ChannelId { get; set; }
    }

    public class SubscriptionEventModel
    {
        /// <summary>
        /// ID of channel where the subscription event occurred
        /// </summary>
        [JsonProperty("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Username of the person who bought a sub
        /// </summary>
        [JsonProperty("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Number of months they've subbed now (e.g. 2 if they bought their 2nd month)
        /// </summary>
        [JsonProperty("months")]
        public int Months { get; set; }
    }

    public class MessageDeletedMessageModel
    {
        /// <summary>
        /// ID of the message that was deleted
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
    }

    public class MessageDeletedEventModel
    {
        /// <summary>
        /// ID of this event (NOT the message to be removed!)
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Message that was deleted
        /// </summary>
        [JsonProperty("message")]
        public required MessageDeletedMessageModel Message { get; set; }
    }

    public class UserBannedUserModel
    {
        /// <summary>
        /// ID of the user. Note it'll be 0 for the janny
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// User's username
        /// </summary>
        [JsonProperty("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Slug suitable for URLs
        /// </summary>
        [JsonProperty("slug")]
        public required string Slug { get; set; }
    }

    public class UserBannedEventModel
    {
        /// <summary>
        /// GUID of the event
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
        /// <summary>
        /// User who was banished
        /// </summary>
        [JsonProperty("user")]
        public required UserBannedUserModel User { get; set; }
        /// <summary>
        /// Janny who did the sweeping
        /// </summary>
        [JsonProperty("banned_by")]
        public required UserBannedUserModel BannedBy { get; set; }
        /// <summary>
        /// Datetime that the ban expires. Null for permabans
        /// </summary>
        [JsonProperty("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class UserUnbannedEventModel
    {
        /// <summary>
        /// GUID of the event
        /// </summary>
        [JsonProperty("id")]
        public required string Id { get; set; }
        /// <summary>
        /// User who was unbanned
        /// </summary>
        [JsonProperty("user")]
        public required UserBannedUserModel User { get; set; }
        /// <summary>
        /// Janny who unbanned
        /// </summary>
        [JsonProperty("unbanned_by")]
        public required UserBannedUserModel UnbannedBy { get; set; }
    }

    public class UpdatedLiveStreamCategoryParentModel
    {
        /// <summary>
        /// ID representing the category
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Slug representing the category
        /// </summary>
        [JsonProperty("slug")]
        public required string Slug { get; set; }
    }

    public class UpdatedLiveStreamCategoryModel
    {
        /// <summary>
        /// ID of the category
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Friendly name of the category
        /// </summary>
        [JsonProperty("name")]
        public required string Name { get; set; }
        /// <summary>
        /// Category's slug for forming URls etc.
        /// </summary>
        [JsonProperty("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Tags for the category
        /// </summary>
        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = [];
        /// <summary>
        /// Parent category, if one is present. I think there usually is one, but made it nullable just in case
        /// </summary>
        [JsonProperty("parent_category")]
        public UpdatedLiveStreamCategoryParentModel? ParentCategory { get; set; }
    }

    public class UpdatedLiveStreamEventModel
    {
        /// <summary>
        /// ID of the livestream (numeric)
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Livestream slug
        /// </summary>
        [JsonProperty("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Livestream title
        /// </summary>
        [JsonProperty("session_title")]
        public required string SessionTitle { get; set; }
        /// <summary>
        /// Livestream start time
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>
        /// Language of the livestream (e.g. English)
        /// </summary>
        [JsonProperty("language")]
        public string? Language { get; set; }
        /// <summary>
        /// Whether the stream is marked as for a mature audience
        /// </summary>
        [JsonProperty("is_mature")]
        public bool IsMature { get; set; }
        /// <summary>
        /// Number of viewers presently watching
        /// </summary>
        [JsonProperty("viewers")]
        public int Viewers { get; set; }
        /// <summary>
        /// Category of the livestream. I believe this is always required but marked it as nullable just in case
        /// </summary>
        [JsonProperty("category")]
        public UpdatedLiveStreamCategoryModel? Category { get; set; }
    }

    public class StopStreamBroadcastLiveStreamChannelModel
    {
        /// <summary>
        /// ID of the channel
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Whether the streamer was sent to ban world
        /// </summary>
        [JsonProperty("is_banned")]
        public bool IsBanned { get; set; }
    }

    public class StopStreamBroadcastLiveStreamModel
    {
        /// <summary>
        /// Livestream event ID
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Channel that stopped streaming
        /// </summary>
        [JsonProperty("channel")]
        public required StopStreamBroadcastLiveStreamChannelModel Channel { get; set; }
    }

    public class StopStreamBroadcastEventModel
    {
        /// <summary>
        /// Object containing information related to the livestream that stopped
        /// </summary>
        [JsonProperty("livestream")]
        public required StopStreamBroadcastLiveStreamModel Livestream { get; set; }
    }

    public class StreamerIsLiveLiveStreamModel
    {
        /// <summary>
        /// ID of the livestream
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// ID of the channel
        /// </summary>
        [JsonProperty("channel_id")]
        public int ChannelId { get; set; }
        /// <summary>
        /// Title of the stream
        /// </summary>
        [JsonProperty("session_title")]
        public required string SessionTitle { get; set; }
        /// <summary>
        /// No idea, just null on my end
        /// </summary>
        [JsonProperty("source")]
        public string? Source { get; set; }
        /// <summary>
        /// Time stream started
        /// </summary>
        [JsonProperty("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class StreamerIsLiveEventModel
    {
        /// <summary>
        /// Object containing information related to the livestream that has just started
        /// </summary>
        [JsonProperty("livestream")]
        public required StreamerIsLiveLiveStreamModel Livestream { get; set; }
    }

    public class PollUpdatePollOptionModel
    {
        /// <summary>
        /// ID of the poll option
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }
        /// <summary>
        /// Label of the poll option
        /// </summary>
        [JsonProperty("label")]
        public required string Label { get; set; }
        /// <summary>
        /// Number of votes the poll option has gotten
        /// </summary>
        [JsonProperty("votes")]
        public int Votes { get; set; }
    }

    public class PollUpdatePollModel
    {
        /// <summary>
        /// Title of the poll
        /// </summary>
        [JsonProperty("title")]
        public required string Title { get; set; }
        /// <summary>
        /// Poll options
        /// </summary>
        [JsonProperty("options")]
        public List<PollUpdatePollOptionModel> Options { get; set; } = [];
        /// <summary>
        /// Duration of the poll in seconds
        /// </summary>
        [JsonProperty("duration")]
        public int Duration { get; set; }
        /// <summary>
        /// Remaining time in seconds
        /// </summary>
        [JsonProperty("remaining")]
        public int Remaining { get; set; }
        /// <summary>
        /// Time in seconds to display the results after completion?
        /// </summary>
        [JsonProperty("result_display_duration")]
        public int ResultDisplayDuration { get; set; }
    }

    public class PollUpdateEventModel
    {
        /// <summary>
        /// Poll data
        /// </summary>
        [JsonProperty("poll")]
        public required PollUpdatePollModel Poll { get; set; }
    }
}