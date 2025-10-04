using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class TempExcludeCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin kasino exclude", RegexOptions.IgnoreCase),
        new Regex(@"^admin kasino exclude (?<user_id>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^admin kasino exclude (?<user_id>\d+) (?<seconds>\d+)$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "Exclude somebody";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        if (!arguments.TryGetValue("user_id", out var userId))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !admin kasino exclude user_id seconds", true);
            return;
        }

        var targetUser = await db.Users.FirstOrDefaultAsync(x => x.KfId == Convert.ToInt32(userId.Value), cancellationToken: ctx);
        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, couldn't find user with that ID", true);
            return;
        }

        var exclusionTime = TimeSpan.FromSeconds(600);
        if (arguments.TryGetValue("seconds", out var seconds))
        {
            exclusionTime = TimeSpan.FromSeconds(Convert.ToInt32(seconds.Value));
        }
        
        var targetGambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);

        if (targetGambler == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, {targetUser.KfUsername} can't be excluded as he's banned.", true);
            return;
        }

        var activeExclusion = await Money.GetActiveExclusionAsync(targetGambler.Id, ctx);
        if (activeExclusion != null)
        {
            var length = DateTimeOffset.UtcNow - activeExclusion.Expires;
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, {targetUser.KfUsername} is already excluded for another {length.Humanize()}",
                true);
            return;
        }

        await db.Exclusions.AddAsync(new GamblerExclusionDbModel
        {
            Gambler = targetGambler,
            Expires = DateTimeOffset.UtcNow.Add(exclusionTime),
            Created = DateTimeOffset.UtcNow,
            Source = ExclusionSource.Administrative
        }, ctx);
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, excluded {targetUser.KfUsername} for {exclusionTime.Humanize()}", true);
    }
}