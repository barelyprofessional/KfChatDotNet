using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using Raffinert.FuzzySharp;

namespace KfChatDotNetBot.Commands;

public class WhoisCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^whois (?<user>.+)")
    ];

    public string? HelpText => "Lookup user IDs by username";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var query = arguments["user"].Value.TrimStart('@').TrimEnd(',').TrimEnd();
        var queryUser = await db.Users.FirstOrDefaultAsync(u => u.KfUsername == query, cancellationToken: ctx);
        if (queryUser != null)
        {
            await botInstance.SendChatMessageAsync($"@{message.Author.Username}, {queryUser.KfUsername}'s ID is {queryUser.KfId}", true);
            return;
        }

        var users = await db.Users.Select(u => u.KfUsername).Distinct().ToListAsync(ctx);
        var result = Process.ExtractOne(query, users);
        queryUser = await db.Users.FirstOrDefaultAsync(u => u.KfUsername == result.Value, cancellationToken: ctx);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, my guess is you're looking for {queryUser!.KfUsername} whose ID is {queryUser.KfId}", true);
    }
}