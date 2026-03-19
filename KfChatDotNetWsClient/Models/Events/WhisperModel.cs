namespace KfChatDotNetWsClient.Models.Events;

public class WhisperModel
{
    public required UserModel Author { get; set; }
    public required UserModel Recipient { get; set; }
    public required string Message { get; set; }
    public required string MessageRaw { get; set; }
    public required DateTimeOffset MessageDate { get; set; }
    public required string MessageRawHtmlDecoded { get; set; }

}