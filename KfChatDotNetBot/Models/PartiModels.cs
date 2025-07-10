using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

public class PartiChannelLiveNotificationModel
{
    [JsonPropertyName("livestream_id")]
    public required int LivestreamId { get; set; }
    [JsonPropertyName("user_id")]
    public required int UserId { get; set; }
    [JsonPropertyName("user_name")]
    public required string Username { get; set; }
    [JsonPropertyName("user_avatar_id")]
    public int UserAvatarId { get; set; }
    [JsonPropertyName("avatar_link")]
    public Uri? AvatarLink { get; set; }
    [JsonPropertyName("event_id")]
    public required int EventId { get; set; }
    [JsonPropertyName("event_title")]
    public required string EventTitle { get; set; }
    [JsonPropertyName("event_file")]
    public Uri? EventFile { get; set; }
    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }
    [JsonPropertyName("viewers_count")]
    public int? ViewersCount { get; set; }
    [JsonPropertyName("social_media")]
    public required string SocialMedia { get; set; }
    [JsonPropertyName("social_username")]
    public required string SocialUsername { get; set; }
    [JsonPropertyName("channel_arn")]
    public required string ChannelArn { get; set; }
    
}