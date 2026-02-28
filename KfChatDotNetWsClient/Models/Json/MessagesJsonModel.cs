using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

// {
//     "messages": [
//     {
//         "author": {
//             "id": 110635,
//             "username": "felted",
//             "avatar_url": "https://kiwifarms.net/data/avatars/m/110/110635.jpg?1657300618"
//         },
//         "message": "Nigger.",
//         "message_id": 4390866,
//         "message_edit_date": 0,
//         "message_date": 1657317093,
//         "message_raw": "Nigger.",
//         "room_id": 10
//     }
//   ]
// }

// message_raw contains the original bbcode for the message
// message is the HTML-version the web client renders with emotes transformed into images, etc.

public class MessagesJsonModel
{
    public class AuthorModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        [JsonPropertyName("avatar_url")]
        public Uri? AvatarUrl { get; set; }
    }

    public class MessageModel
    {
        [JsonPropertyName("author")]
        public required AuthorModel Author { get; set; }
        [JsonPropertyName("message")]
        public required string Message { get; set; }
        [JsonPropertyName("message_uuid")]
        public required string MessageUuid { get; set; }
        [JsonPropertyName("message_edit_date")]
        public int MessageEditDate { get; set; }
        [JsonPropertyName("message_date")]
        public int MessageDate { get; set; }
        [JsonPropertyName("message_raw")]
        public required string MessageRaw { get; set; }
        [JsonPropertyName("room_id")]
        public int RoomId { get; set; }
    }

    [JsonPropertyName("messages")] public List<MessageModel> Messages { get; set; } = [];
}