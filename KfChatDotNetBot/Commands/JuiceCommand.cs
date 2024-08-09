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