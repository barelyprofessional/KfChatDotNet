using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

public class TimeCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^time")];
    public string? HelpText => "Get current time in BMT";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var bmt = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset);
        await botInstance.SendChatMessageAsync($"It's currently {bmt:h:mm:ss tt} BMT");
    }
}