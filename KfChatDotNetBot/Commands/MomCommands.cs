using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class MomCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^mom")];
    public string HelpText => "DTPN!";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        db.Users.Attach(user);
        var momSettings = await SettingsProvider.GetMultipleValuesAsync(
            [BuiltIn.Keys.MomCooldown, BuiltIn.Keys.TwitchBossmanJackUsername, BuiltIn.Keys.KiwiFarmsRedColor]);
        var cooldown = momSettings[BuiltIn.Keys.MomCooldown].ToType<int>();
        var lastMom = (await db.Moms.ToListAsync(ctx)).OrderByDescending(j => j.Time).Take(1).ToList();
        if (lastMom.Count == 0 || (lastMom[0].Time.AddSeconds(cooldown) - DateTimeOffset.UtcNow).TotalSeconds <= 0)
        {
            if (user.UserRight > UserRight.Loser)
            {
                await db.Moms.AddAsync(new MomDbModel { User = user, Time = DateTimeOffset.UtcNow }, ctx);
                await db.SaveChangesAsync(ctx);
            }
            var count = await db.Moms.CountAsync(ctx);
            await botInstance.SendChatMessageAsync(
                $"[b][color={momSettings[BuiltIn.Keys.KiwiFarmsRedColor].Value}]DTPN![/color][/b] - {momSettings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} has fucked {count:N0} MILFs!",
                true);
            return;
        }

        var secondsRemaining = lastMom[0].Time.AddSeconds(cooldown) - DateTimeOffset.UtcNow;
        await botInstance.SendChatMessageAsync(
            $"{momSettings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} (one pump chump) needs {secondsRemaining.Humanize(precision: 2, minUnit: TimeUnit.Millisecond)} rest before he can fuck another MILF",
            true);
    }
}