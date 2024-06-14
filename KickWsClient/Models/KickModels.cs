using System.Text.Json.Serialization;

namespace KickWsClient.Models;

public class KickModels
{
    public class ChatMessageSenderIdentityBadgeModel
    {
        /// <summary>
        /// Internal type for badge e.g. moderator
        /// </summary>
        [JsonPropertyName("type")]
        public required string Type { get; set; }
        /// <summary>
        /// Friendly name for badge e.g. Moderator
        /// </summary>
        [JsonPropertyName("text")]
        public required string Text { get; set; }
        /// <summary>
        /// Count (if applicable) for badge (e.g. sub count for gifted subs)
        /// </summary>
        [JsonPropertyName("count")]
        public int? Count { get; set; }
    }
    
    public class ChatMessageSenderIdentityModel
    {
        /// <summary>
        /// User's hex color
        /// </summary>
        [JsonPropertyName("color")]
        public required string Color { get; set; }

        /// <summary>
        /// Badges a user has
        /// </summary>
        [JsonPropertyName("badges")]
        public List<ChatMessageSenderIdentityBadgeModel> Badges = [];
    }
    
    public class ChatMessageSenderModel
    {
        /// <summary>
        /// Kick internal user ID
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Kick display name
        /// </summary>
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Kick slug (for URLs)
        /// </summary>
        [JsonPropertyName("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Identity info for display color and badges
        /// </summary>
        [JsonPropertyName("identity")]
        public required ChatMessageSenderIdentityModel Identity { get; set; }
    }

    public class ChatMessageMetadataOriginalSenderModel
    {
        /// <summary>
        /// Original sender's user ID
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Original sender's username
        /// </summary>
        [JsonPropertyName("username")]
        public required string Username { get; set; }
    }

    public class ChatMessageMetadataOriginalMessageModel
    {
        /// <summary>
        /// ID (GUID) of the original message
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Content of the original message
        /// </summary>
        [JsonPropertyName("content")]
        public required string Content { get; set; }
    }

    public class ChatMessageMetadataModel
    {
        /// <summary>
        /// Sender of the message that this message is in reply to
        /// </summary>
        [JsonPropertyName("original_sender")]
        public required ChatMessageMetadataOriginalSenderModel OriginalSender { get; set; }
        /// <summary>
        /// Content of the message that this message is in reply to
        /// </summary>
        [JsonPropertyName("original_message")]
        public required ChatMessageMetadataOriginalMessageModel OriginalMessage { get; set; }
    }
    
    public class FollowersUpdatedEventModel
    {
        /// <summary>
        /// Channel follower count
        /// </summary>
        [JsonPropertyName("followersCount")]
        public int FollowersCount { get; set; }
        /// <summary>
        /// ID to identify what chatroom this event belongs to
        /// </summary>
        [JsonPropertyName("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Maybe returns your username if you're auth'd? No idea. Just returned null for me
        /// </summary>
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        /// <summary>
        /// Epoch value that signifies ???
        /// </summary>
        [JsonPropertyName("created_at")]
        public int? CreatedAtEpoch { get; set; }
        // It returned true even though I'm not signed in which makes no sense, so I'll assume there's a chance it'll
        // suddenly appear and mark as nullable as it's not really a useful property anyway.
        /// <summary>
        /// Does it mean we're following? Who knows, returns true even if you're a guest
        /// </summary>
        [JsonPropertyName("followed")]
        public bool? Followed { get; set; }
    }

    public class ChatMessageEventModel
    {
        /// <summary>
        /// Message unique GUID that's referenced for replies and deletions
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Chatroom ID you can use to differentiate this from other rooms if you sub to multiple at a time
        /// </summary>
        [JsonPropertyName("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Content of the message. Emotes are encoded like [emote:161238:russW] which translates to -> https://files.kick.com/emotes/161238/fullsize
        /// </summary>
        [JsonPropertyName("content")]
        public required string Content { get; set; }
        /// <summary>
        /// Regular message is 'message', replies are 'reply'
        /// </summary>
        [JsonPropertyName("type")]
        public required string Type { get; set; }
        // Why created at is an epoch for followers updated but ISO8601 for chat messages is just a mystery
        /// <summary>
        /// Time message was sent
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>
        /// Sender of the message
        /// </summary>
        [JsonPropertyName("sender")]
        public required ChatMessageSenderModel Sender { get; set; }
        /// <summary>
        /// Message metadata which is set for replies only
        /// </summary>
        [JsonPropertyName("metadata")]
        public ChatMessageMetadataModel? Metadata { get; set; }
    }

    public class ChannelSubscriptionEventModel
    {
        /// <summary>
        /// User IDs of subscription recipients
        /// </summary>
        [JsonPropertyName("user_ids")]
        public List<int> UserIds { get; set; } = [];
        /// <summary>
        /// Username of the person who subbed / gifted
        /// </summary>
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Channel ID where the sub event occurred
        /// </summary>
        [JsonPropertyName("channel_id")]
        public int ChannelId { get; set; }
    }

    public class SubscriptionEventModel
    {
        /// <summary>
        /// ID of channel where the subscription event occurred
        /// </summary>
        [JsonPropertyName("chatroom_id")]
        public int ChatroomId { get; set; }
        /// <summary>
        /// Username of the person who bought a sub
        /// </summary>
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Number of months they've subbed now (e.g. 2 if they bought their 2nd month)
        /// </summary>
        [JsonPropertyName("months")]
        public int Months { get; set; }
    }

    public class MessageDeletedMessageModel
    {
        /// <summary>
        /// ID of the message that was deleted
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
    }

    public class MessageDeletedEventModel
    {
        /// <summary>
        /// ID of this event (NOT the message to be removed!)
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        /// <summary>
        /// Message that was deleted
        /// </summary>
        [JsonPropertyName("message")]
        public required MessageDeletedMessageModel Message { get; set; }
    }

    public class UserBannedUserModel
    {
        /// <summary>
        /// ID of the user. Note it'll be 0 for the janny
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// User's username
        /// </summary>
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        /// <summary>
        /// Slug suitable for URLs
        /// </summary>
        [JsonPropertyName("slug")]
        public required string Slug { get; set; }
    }

    public class UserBannedEventModel
    {
        /// <summary>
        /// GUID of the event
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        /// <summary>
        /// User who was banished
        /// </summary>
        [JsonPropertyName("user")]
        public required UserBannedUserModel User { get; set; }
        /// <summary>
        /// Janny who did the sweeping
        /// </summary>
        [JsonPropertyName("banned_by")]
        public required UserBannedUserModel BannedBy { get; set; }
        /// <summary>
        /// Datetime that the ban expires. Null for permabans
        /// </summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class UserUnbannedEventModel
    {
        /// <summary>
        /// GUID of the event
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        /// <summary>
        /// User who was unbanned
        /// </summary>
        [JsonPropertyName("user")]
        public required UserBannedUserModel User { get; set; }
        /// <summary>
        /// Janny who unbanned
        /// </summary>
        [JsonPropertyName("unbanned_by")]
        public required UserBannedUserModel UnbannedBy { get; set; }
    }

    public class UpdatedLiveStreamCategoryParentModel
    {
        /// <summary>
        /// ID representing the category
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Slug representing the category
        /// </summary>
        [JsonPropertyName("slug")]
        public required string Slug { get; set; }
    }

    public class UpdatedLiveStreamCategoryModel
    {
        /// <summary>
        /// ID of the category
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Friendly name of the category
        /// </summary>
        [JsonPropertyName("name")]
        public required string Name { get; set; }
        /// <summary>
        /// Category's slug for forming URls etc.
        /// </summary>
        [JsonPropertyName("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Tags for the category
        /// </summary>
        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = [];
        /// <summary>
        /// Parent category, if one is present. I think there usually is one, but made it nullable just in case
        /// </summary>
        [JsonPropertyName("parent_category")]
        public UpdatedLiveStreamCategoryParentModel? ParentCategory { get; set; }
    }

    public class UpdatedLiveStreamEventModel
    {
        /// <summary>
        /// ID of the livestream (numeric)
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Livestream slug
        /// </summary>
        [JsonPropertyName("slug")]
        public required string Slug { get; set; }
        /// <summary>
        /// Livestream title
        /// </summary>
        [JsonPropertyName("session_title")]
        public required string SessionTitle { get; set; }
        /// <summary>
        /// Livestream start time
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>
        /// Language of the livestream (e.g. English)
        /// </summary>
        [JsonPropertyName("language")]
        public string? Language { get; set; }
        /// <summary>
        /// Whether the stream is marked as for a mature audience
        /// </summary>
        [JsonPropertyName("is_mature")]
        public bool IsMature { get; set; }
        /// <summary>
        /// Number of viewers presently watching
        /// </summary>
        [JsonPropertyName("viewers")]
        public int Viewers { get; set; }
        /// <summary>
        /// Category of the livestream. I believe this is always required but marked it as nullable just in case
        /// </summary>
        [JsonPropertyName("category")]
        public UpdatedLiveStreamCategoryModel? Category { get; set; }
    }

    public class StopStreamBroadcastLiveStreamChannelModel
    {
        /// <summary>
        /// ID of the channel
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Whether the streamer was sent to ban world
        /// </summary>
        [JsonPropertyName("is_banned")]
        public bool IsBanned { get; set; }
    }

    public class StopStreamBroadcastLiveStreamModel
    {
        /// <summary>
        /// Livestream event ID
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Channel that stopped streaming
        /// </summary>
        [JsonPropertyName("channel")]
        public required StopStreamBroadcastLiveStreamChannelModel Channel { get; set; }
    }

    public class StopStreamBroadcastEventModel
    {
        /// <summary>
        /// Object containing information related to the livestream that stopped
        /// </summary>
        [JsonPropertyName("livestream")]
        public required StopStreamBroadcastLiveStreamModel Livestream { get; set; }
    }

    public class StreamerIsLiveLiveStreamModel
    {
        /// <summary>
        /// ID of the livestream
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// ID of the channel
        /// </summary>
        [JsonPropertyName("channel_id")]
        public int ChannelId { get; set; }
        /// <summary>
        /// Title of the stream
        /// </summary>
        [JsonPropertyName("session_title")]
        public required string SessionTitle { get; set; }
        /// <summary>
        /// No idea, just null on my end
        /// </summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }
        /// <summary>
        /// Time stream started
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class StreamerIsLiveEventModel
    {
        /// <summary>
        /// Object containing information related to the livestream that has just started
        /// </summary>
        [JsonPropertyName("livestream")]
        public required StreamerIsLiveLiveStreamModel Livestream { get; set; }
    }

    public class PollUpdatePollOptionModel
    {
        /// <summary>
        /// ID of the poll option
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }
        /// <summary>
        /// Label of the poll option
        /// </summary>
        [JsonPropertyName("label")]
        public required string Label { get; set; }
        /// <summary>
        /// Number of votes the poll option has gotten
        /// </summary>
        [JsonPropertyName("votes")]
        public int Votes { get; set; }
    }

    public class PollUpdatePollModel
    {
        /// <summary>
        /// Title of the poll
        /// </summary>
        [JsonPropertyName("title")]
        public required string Title { get; set; }
        /// <summary>
        /// Poll options
        /// </summary>
        [JsonPropertyName("options")]
        public List<PollUpdatePollOptionModel> Options { get; set; } = [];
        /// <summary>
        /// Duration of the poll in seconds
        /// </summary>
        [JsonPropertyName("duration")]
        public int Duration { get; set; }
        /// <summary>
        /// Remaining time in seconds
        /// </summary>
        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }
        /// <summary>
        /// Time in seconds to display the results after completion?
        /// </summary>
        [JsonPropertyName("result_display_duration")]
        public int ResultDisplayDuration { get; set; }
    }

    public class PollUpdateEventModel
    {
        /// <summary>
        /// Poll data
        /// </summary>
        [JsonPropertyName("poll")]
        public required PollUpdatePollModel Poll { get; set; }
    }
}