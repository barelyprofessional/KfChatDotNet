using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Models;
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
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        // Have to attach the entity because it is coming from another DB context
        // https://stackoverflow.com/questions/52718652/ef-core-sqlite-sqlite-error-19-unique-constraint-failed
        db.Users.Attach(user);
        var juicerSettings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.JuiceAmount, BuiltIn.Keys.JuiceCooldown, BuiltIn.Keys.JuiceLoserDivision,
            BuiltIn.Keys.GambaSeshDetectEnabled, BuiltIn.Keys.JuiceAllowedWhileStreaming,
            BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.JuiceAutoDeleteMsgDelay,
            BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive
        ]);
        var cooldown = juicerSettings[BuiltIn.Keys.JuiceCooldown].ToType<int>();
        var amount = juicerSettings[BuiltIn.Keys.JuiceAmount].ToType<int>();
        if (user.UserRight == UserRight.Loser) amount /= juicerSettings[BuiltIn.Keys.JuiceLoserDivision].ToType<int>();
        var lastJuicer = (await db.Juicers.Where(j => j.User == user).ToListAsync(ctx)).OrderByDescending(j => j.JuicedAt).Take(1).ToList();
        if (!botInstance.GambaSeshPresent && juicerSettings[BuiltIn.Keys.GambaSeshDetectEnabled].ToBoolean())
        {
            await botInstance.SendChatMessageAsync("Looks like GambaSesh isn't here. If he is, get him to say something then try again.", true);
            return;
        }

        if (!juicerSettings[BuiltIn.Keys.JuiceAllowedWhileStreaming].ToBoolean() && juicerSettings[BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive].ToBoolean())
        {
            await botInstance.SendChatMessageAsync(
                $"No juicers permitted while {juicerSettings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} is live!", true);
            return;
        }
        
        if (lastJuicer.Count == 0 || (lastJuicer[0].JuicedAt.AddSeconds(cooldown) - DateTimeOffset.UtcNow).TotalSeconds <= 0)
        {
            TimeSpan? autoDeleteAfter = null;
            if (juicerSettings[BuiltIn.Keys.JuiceAutoDeleteMsgDelay].Value != null)
            {
                autoDeleteAfter =
                    TimeSpan.FromSeconds(juicerSettings[BuiltIn.Keys.JuiceAutoDeleteMsgDelay].ToType<int>());
            }
            await botInstance.SendChatMessageAsync($"!juice {message.Author.Id} {amount}", true, autoDeleteAfter: autoDeleteAfter);
            await db.Juicers.AddAsync(new JuicerDbModel
                { Amount = amount, User = user, JuicedAt = DateTimeOffset.UtcNow }, ctx);
            await db.SaveChangesAsync(ctx);
            return;
        }

        var secondsRemaining = lastJuicer[0].JuicedAt.AddSeconds(cooldown) - DateTimeOffset.UtcNow;

        await botInstance.SendChatMessageAsync($"You gotta wait {secondsRemaining.Humanize(precision: 2, minUnit: TimeUnit.Second)} for another juicer", true);
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