using Newtonsoft.Json;

namespace KfChatDotNetWsClient.Models.Json;

public class DeleteMessagesJsonModel
{
    [JsonProperty("delete")]
    public List<int> MessageIdsToDelete { get; set; }
}