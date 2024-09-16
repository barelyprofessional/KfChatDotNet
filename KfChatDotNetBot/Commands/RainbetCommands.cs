using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class RainbetStatsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^rainbet stats (?<window>\d+)$")
    ];
    public string? HelpText => "Get betting statistics in the given window";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var window = Convert.ToInt32(arguments["window"].Value);
        var start = DateTimeOffset.UtcNow.AddHours(-window);
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.RainbetBets.ToListAsync(ctx)).Where(b => b.UpdatedAt.UtcDateTime > start).ToList();
        if (bets.Count == 0)
        {
            await botInstance.SendChatMessageAsync("No bets captured during this window", true);
            return;
        }
        var output = $"Rainbet stats for the last {window} hours (as seen on the bet feed):[br]" +
                     $"Bets: {bets.Count:N0}; Payout: {bets.Sum(b => b.Payout):C}; Wagered: {bets.Sum(b => b.Value):C}";
        await botInstance.SendChatMessageAsync(output, true);
    }
}

public class RainbetRecentBetCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^rainbet recent$")
    ];
    public string? HelpText => "Get the most recent 3 bets";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings = await Helpers.GetMultipleValues([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor, BuiltIn.Keys.HowlggDivisionAmount
        ]);
        await using var db = new ApplicationDbContext();
        // EF SQLite doesn't support filtering on dates :(
        var bets = (await db.RainbetBets.ToListAsync(ctx)).OrderByDescending(j => j.UpdatedAt).Take(3).ToList();
        var output = "Most recent 3 bets on Rainbet (as seen on the bet feed):";
        foreach (var bet in bets)
        {
            var color = settings[BuiltIn.Keys.KiwiFarmsGreenColor].Value;
            if (bet.Payout < bet.Value) color = settings[BuiltIn.Keys.KiwiFarmsRedColor].Value;
            output += $"[br]Value: {bet.Value:C}; Payout: [color={color}]{bet.Payout:C}[/color]; Multi: {bet.Multiplier:N2}x; Game: {bet.GameName}; {(DateTimeOffset.UtcNow - bet.UpdatedAt).Humanize(precision: 1)} ago";
        }
        await botInstance.SendChatMessageAsync(output, true);
    }
}