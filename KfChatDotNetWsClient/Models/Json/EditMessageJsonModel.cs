using Newtonsoft.Json;

namespace KfChatDotNetWsClient.Models.Json;

public class EditMessageJsonModel
{
    [JsonProperty("id")]
    public int Id { get; set; }
    
    [JsonProperty("message")]
    public string Message { get; set; }
}