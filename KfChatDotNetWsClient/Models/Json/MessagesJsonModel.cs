using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("avatar_url")]
        public Uri AvatarUrl { get; set; }
    }

    public class MessageModel
    {
        [JsonProperty("author")]
        public AuthorModel Author { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("message_id")]
        public int MessageId { get; set; }
        [JsonProperty("message_edit_date")]
        public int MessageEditDate { get; set; }
        [JsonProperty("message_date")]
        public int MessageDate { get; set; }
        [JsonProperty("message_raw")]
        public string MessageRaw { get; set; }
        [JsonProperty("room_id")]
        public int RoomId { get; set; }
    }
    
    [JsonProperty("messages")]
    public List<MessageModel> Messages { get; set; }
}