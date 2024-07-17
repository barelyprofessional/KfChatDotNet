using System.Text.RegularExpressions;
using KfChatDotNetKickBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetKickBot.Commands;

public class TimeCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^time")];
    public string HelpText => "Get current time in BMT";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(KickBot botInstance, MessageModel message, GroupCollection arguments, CancellationToken ctx)
    {
        var bmt = new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")), TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").BaseUtcOffset);
        botInstance.SendChatMessage($"It's currently {bmt:h:mm:ss tt} BMT");
    }
}