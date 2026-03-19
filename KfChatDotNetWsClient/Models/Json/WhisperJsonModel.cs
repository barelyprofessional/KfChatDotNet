using System.Text.Json.Serialization;

namespace KfChatDotNetWsClient.Models.Json;

// {
//     "whisper": {
//         "author": {
//             "id": 58227,
//             "username": "Flaming Dumpster",
//             "avatar_url": "/data/avatars/m/58/58227.jpg?1771372231"
//         },
//         "recipient": {
//             "id": 1,
//             "username": "Null",
//             "avatar_url": "/data/avatars/m/0/1.jpg?1767201853"
//         },
//         "message": "nigger",
//         "message_raw": "nigger",
//         "message_date": 1773881876
//     }
// }
public class WhisperJsonModel
{
    [JsonPropertyName("author")]
    public required MessagesJsonModel.AuthorModel Author { get; set; }
    [JsonPropertyName("recipient")]
    public required MessagesJsonModel.AuthorModel Recipient { get; set; }
    [JsonPropertyName("message")]
    public required string Message { get; set; }
    [JsonPropertyName("message_raw")]
    public required string MessageRaw { get; set; }
    [JsonPropertyName("message_date")]
    public required int MessageDate { get; set; }
}