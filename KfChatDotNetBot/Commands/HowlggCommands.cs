using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class HowlggStatsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^howl stats (?<window>\d+)$")
    ];
    public string? HelpText => "Get betting statistics in the given window";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var window = Convert.ToInt32(arguments["window"].Value);
        var start = DateTimeOffset.UtcNow.AddHours(-window);
        var division = (await Helpers.GetValue(BuiltIn.Keys.HowlggDivisionAmount)).ToType<float>();
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.HowlggBets.ToListAsync(ctx)).Where(b => b.Date.UtcDateTime > start).ToList();
        if (bets.Count == 0)
        {
            await botInstance.SendChatMessageAsync("No bets captured during this window", true);
            return;
        }
        var output = $"Howl.gg stats for the last {window} hours:[br]" +
                     $"Bets: {bets.Count:N0}; Profit: {bets.Sum(b => b.Profit) / division:C}; Wagered: {bets.Sum(b => b.Bet) / division:C}";
        await botInstance.SendChatMessageAsync(output, true);
    }
}

public class HowlggRecentBetCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^howl recent$")
    ];
    public string? HelpText => "Get the most recent 3 bets";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings = await Helpers.GetMultipleValues([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor, BuiltIn.Keys.HowlggDivisionAmount
        ]);
        var division = settings[BuiltIn.Keys.HowlggDivisionAmount].ToType<float>();
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.HowlggBets.ToListAsync(ctx)).OrderByDescending(j => j.Date).Take(3).ToList();
        var output = "Most recent 3 bets on Howl.gg:";
        foreach (var bet in bets)
        {
            var color = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
            if (bet.Profit < 0) color = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
            output += $"[br]Bet: {bet.Bet / division:C}; Profit: [color={color}]{bet.Profit / division:C}[/color]; Game: {bet.Game.Humanize()}; {(DateTimeOffset.UtcNow - bet.Date).Humanize(precision: 1)} ago";
        }
        await botInstance.SendChatMessageAsync(output, true);
    }
}