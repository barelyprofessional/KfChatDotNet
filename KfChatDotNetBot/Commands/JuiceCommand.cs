using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class JuiceStatsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^juice stats$"),
        new Regex(@"^juice stats (?<top>\d+)$")
    ];
    public string HelpText => "Get juice stats!";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        int top;
        if (arguments.TryGetValue("top", out var argument))
        {
            top = Convert.ToInt32(argument.Value);
            if (top > 10)
            {
                await botInstance.SendChatMessageAsync($"'{top}' exceeds the limit on the amount of leeches you can return", true);
                return;
            }
        }
        else
        {
            top = 3;
        }
        
        await using var db = new ApplicationDbContext();
        var topLeeches = await db.Juicers.GroupBy(u => u.User).Select(l => new
        {
            User = l.Key.KfUsername,
            Amount = l.Sum(a => a.Amount)
        }).OrderByDescending(x => x.Amount).Take(top).ToListAsync(ctx);
        var sum = await db.Juicers.SumAsync(s => s.Amount, cancellationToken: ctx);
        var count = await db.Juicers.CountAsync(ctx);
        var totalUsers = await db.Juicers.Select(j => j.User).Distinct().CountAsync(ctx);
        var msg = $"Total Juicers: {count:N0}; Total Handed Out: {sum:C0}; Total Users Juiced: {totalUsers:N0}; Average Juicers / User: {count / totalUsers:N0}[br]Top Leeches: ";
        var i = 0;
        foreach (var leech in topLeeches)
        {
            i++;
            msg += $"[b]{i}.[/b] {leech.User} with {leech.Amount:C0} juiced; ";
        }

        await botInstance.SendChatMessageAsync(msg.TrimEnd().TrimEnd(';'), true);
    }
}