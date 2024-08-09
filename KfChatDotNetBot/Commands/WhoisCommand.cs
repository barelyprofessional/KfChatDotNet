using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class WhoisCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^whois (?<user>.+)")
    ];

    public string HelpText => "Lookup user IDs by username";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var query = arguments["user"].Value.TrimStart('@').TrimEnd(',').TrimEnd();
        var queryUser = await db.Users.FirstOrDefaultAsync(u => u.KfUsername == query, cancellationToken: ctx);
        if (queryUser == null)
        {
            botInstance.SendChatMessage($"Requested user '{query}' does not exist. (Note this is case-sensitive)", true);
            return;
        }
        botInstance.SendChatMessage($"@{message.Author.Username}, {queryUser.KfUsername}'s ID is {queryUser.KfId}", true);
    }
}