using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class JuiceCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^juiceme")];
    public string HelpText => "Get juice!";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        // Have to attach the entity because it is coming from another DB context
        // https://stackoverflow.com/questions/52718652/ef-core-sqlite-sqlite-error-19-unique-constraint-failed
        db.Users.Attach(user);
        var juicerSettings = await Helpers.GetMultipleValues([BuiltIn.Keys.JuiceAmount, BuiltIn.Keys.JuiceCooldown]);
        var cooldown = juicerSettings[BuiltIn.Keys.JuiceCooldown].ToType<int>();
        var amount = juicerSettings[BuiltIn.Keys.JuiceAmount].ToType<int>();
        var lastJuicer = (await db.Juicers.Where(j => j.User == user).ToListAsync(ctx)).OrderByDescending(j => j.JuicedAt).Take(1).ToList();
        if (lastJuicer.Count == 0)
        {
            botInstance.SendChatMessage($"!juice {message.Author.Id} {amount}", true);
            await db.Juicers.AddAsync(new JuicerDbModel
                { Amount = amount, User = user, JuicedAt = DateTimeOffset.UtcNow }, ctx);
            await db.SaveChangesAsync(ctx);
            return;
        }

        var secondsRemaining = lastJuicer[0].JuicedAt.AddSeconds(cooldown) - DateTimeOffset.UtcNow;
        if (secondsRemaining.TotalSeconds <= 0)
        {
            botInstance.SendChatMessage($"!juice {message.Author.Id} {amount}", true);
            await db.Juicers.AddAsync(new JuicerDbModel
                { Amount = amount, User = user, JuicedAt = DateTimeOffset.UtcNow }, ctx);
            await db.SaveChangesAsync(ctx);
            return;
        }

        botInstance.SendChatMessage($"You gotta wait {secondsRemaining.Humanize(precision: 2, minUnit: TimeUnit.Second)} for another juicer", true);
    }
}

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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        int top;
        if (arguments.TryGetValue("top", out var argument))
        {
            top = Convert.ToInt32(argument.Value);
            if (top > 10)
            {
                botInstance.SendChatMessage($"'{top}' exceeds the limit on the amount of leeches you can return", true);
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

        botInstance.SendChatMessage(msg.TrimEnd().TrimEnd(';'), true);
    }
}