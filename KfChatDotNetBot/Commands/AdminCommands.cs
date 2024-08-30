using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class SetRightCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin role set (?<user>\d+) (?<right>\d+)$"),
        new Regex(@"^admin right set (?<user>\d+) (?<right>\d+)$")
    ];

    public string? HelpText => "Set a user's right";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var targetUserId = Convert.ToInt32(arguments["user"].Value);
        var right = (UserRight)Convert.ToInt32(arguments["right"].Value);
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == targetUserId, cancellationToken: ctx);
        if (targetUser == null)
        {
            botInstance.SendChatMessage($"User '{targetUserId}' does not exist", true);
            return;
        }

        targetUser.UserRight = right;
        await db.SaveChangesAsync(ctx);
        botInstance.SendChatMessage($"@{message.Author.Username}, {targetUser.KfUsername}'s right set to {right.Humanize()}", true);
    }
}