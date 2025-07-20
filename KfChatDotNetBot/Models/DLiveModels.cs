namespace KfChatDotNetBot.Models;

public class DLiveIsLiveModel
{
    public required bool IsLive { get; set; }
    public string? Title { get; set; }
    public required string Username { get; set; }
}