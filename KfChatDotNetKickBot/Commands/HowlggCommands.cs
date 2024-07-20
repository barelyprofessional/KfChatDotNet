using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetKickBot.Models.DbModels;
using KfChatDotNetKickBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetKickBot.Commands;

public class HowlggStatsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^howl stats (?<window>\d+)$")
    ];
    public string HelpText => "Get betting statistics in the given window";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;
    public async Task RunCommand(KickBot botInstance, MessageModel message, GroupCollection arguments, CancellationToken ctx)
    {
        var window = Convert.ToInt32(arguments["window"].Value);
        var start = DateTimeOffset.UtcNow.AddHours(-window);
        var division = (await Helpers.GetValue(BuiltIn.Keys.HowlggDivisionAmount)).ToType<float>();
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.HowlggBets.ToListAsync(ctx)).Where(b => b.Date.UtcDateTime > start).ToList();
        if (bets.Count == 0)
        {
            botInstance.SendChatMessage("No bets captured during this window", true);
            return;
        }
        var output = $"Howl.gg stats for the last {window} hours:[br]" +
                     $"Bets: {bets.Count:N0}; Profit: {bets.Sum(b => b.Profit) / division:C}; Wagered: {bets.Sum(b => b.Bet) / division:C}";
        botInstance.SendChatMessage(output, true);
    }
}

public class HowlggRecentBetCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^howl recent$")
    ];
    public string HelpText => "Get the most recent 3 bets";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;
    public async Task RunCommand(KickBot botInstance, MessageModel message, GroupCollection arguments, CancellationToken ctx)
    {
        var division = (await Helpers.GetValue(BuiltIn.Keys.HowlggDivisionAmount)).ToType<float>();
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.HowlggBets.ToListAsync(ctx)).OrderByDescending(j => j.Date).Take(3).ToList();
        var output = "Most recent 3 bets on Howl.gg:";
        foreach (var bet in bets)
        {
            var color = "#3dd179";
            if (bet.Profit < 0) color = "#f1323e";
            output += $"[br]Bet: {bet.Bet / division:C}; Profit: [color={color}]{bet.Profit / division:C}[/color]; Game: {bet.Game.Humanize()}; {(DateTimeOffset.UtcNow - bet.Date).Humanize(precision: 1)} ago";
        }
        botInstance.SendChatMessage(output, true);
    }
}