using System.Text.Json.Serialization;
namespace KfChatDotNetWsClient.Models.Json;

// {
//     "users": {
//         "1337": {
//             "id": 1337,
//             "username": "Example User",
//             "avatar_url": "https://kiwifarms.net/data/avatars/m/13/1337.jpg?1648885311",
//             "last_activity": 1657316000
//         }
//     }
// }

public class UsersJsonModel
{
    public class UserModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("username")]
        public required string Username { get; set; }
        [JsonPropertyName("avatar_url")]
        public Uri? AvatarUrl { get; set; }
        [JsonPropertyName("last_activity")]
        public int LastActivity { get; set; }
    }

    [JsonPropertyName("users")]
    public Dictionary<string, UserModel> Users { get; set; } = new();
}