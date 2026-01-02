using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;

using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

public class DailyDollar : ICommand
{
    public List<Regex> Patterns => [
        
        new Regex(@"^dailydollar$", RegexOptions.IgnoreCase),
        new Regex("^dailydollar")
    ];
    public string? HelpText => "get 100 KKK once a day";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromHours(24)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        await botInstance.SendChatMessageAsync($"!juice {user.Id} 100");
    }
}
