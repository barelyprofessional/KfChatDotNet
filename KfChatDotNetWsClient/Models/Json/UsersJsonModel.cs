using Newtonsoft.Json;

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
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("avatar_url")]
        public Uri AvatarUrl { get; set; }
        [JsonProperty("last_activity")]
        public int LastActivity { get; set; }
    }
    
    [JsonProperty("users")]
    public Dictionary<string, UserModel> Users { get; set; }
}