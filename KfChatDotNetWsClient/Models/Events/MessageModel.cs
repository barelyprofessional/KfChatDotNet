namespace KfChatDotNetWsClient.Models.Events;

public class MessageModel
{
    public UserModel Author { get; set; }
    public string Message { get; set; }
    public int MessageId { get; set; }
    public DateTimeOffset? MessageEditDate { get; set; }
    public DateTimeOffset MessageDate { get; set; }
    public string MessageRaw { get; set; }
    public int RoomId { get; set; }
}