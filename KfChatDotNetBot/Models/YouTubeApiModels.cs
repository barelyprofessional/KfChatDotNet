using System.Text.Json.Serialization;

namespace KfChatDotNetBot.Models;

    public class YouTubeApiModels
    {
        public class ContentDetailsRoot
        {
            [JsonPropertyName("kind")]
            public required string Kind { get; set; }
            [JsonPropertyName("items")]
            public required List<ItemModel> Items { get; set; }
        }

        public class SnippetModel
        {
            [JsonPropertyName("publishedAt")]
            public required DateTime PublishedAt { get; set; }
            [JsonPropertyName("channelId")]
            public required string ChannelId { get; set; }
            [JsonPropertyName("title")]
            public required string Title { get; set; }
            [JsonPropertyName("description")]
            public required string Description { get; set; }
            [JsonPropertyName("channelTitle")]
            public required string ChannelTitle { get; set; }
            // "none", "live", "upcoming"
            [JsonPropertyName("liveBroadcastContent")]
            public required string LiveBroadcastContent { get; set; }
        }

        public class ItemModel
        {
            [JsonPropertyName("kind")]
            public required string Kind { get; set; }
            [JsonPropertyName("id")]
            public required string Id { get; set; }
            [JsonPropertyName("snippet")]
            public required SnippetModel Snippet { get; set; }
        }
    }