using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
public class GetBiggestWins : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^kasino bigwins", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Big wins for the current gameday";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var gameDay = await Money.GetKasinoDate();
        var wagers = await db.Wagers.Where(x => x.TimeUnixEpochSeconds > gameDay.ToUnixTimeSeconds())
            .Include(x => x.Gambler).ThenInclude(x => x.User).ToListAsync(ctx);
        var biggestMultees = wagers.OrderByDescending(x => x.Multiplier).Take(10).ToList();
        var biggestWins = wagers.OrderByDescending(x => x.WagerEffect).Take(10).ToList();
        var multeesMsg =
            $"Big multees adding up to {await biggestMultees.Sum(x => x.WagerEffect).FormatKasinoCurrencyAsync()}:";
        var i = 0;
        foreach (var win in biggestMultees)
        {
            i++;
            multeesMsg += $"[br]{i}. {win.Gambler.User.KfUsername} bet {await win.WagerAmount.FormatKasinoCurrencyAsync()} on {win.Game.Humanize()} and won {await win.WagerEffect.FormatKasinoCurrencyAsync()} ({win.Multiplier:N}x)";
        }
        var bigWinsMsg = $"Big wins adding up to {await biggestWins.Sum(x => x.WagerEffect).FormatKasinoCurrencyAsync()}:";
        i = 0;
        foreach (var win in biggestWins)
        {
            i++;
            bigWinsMsg += $"[br]{i}. {win.Gambler.User.KfUsername} bet {await win.WagerAmount.FormatKasinoCurrencyAsync()} on {win.Game.Humanize()} and won {await win.WagerEffect.FormatKasinoCurrencyAsync()} ({win.Multiplier:N}x)";
        }

        await botInstance.SendChatMessageAsync($"Top 10 biggest wins for game day {gameDay:yyyy-MM-dd}" +
            $"[br]Big Multees[br][spoiler]{multeesMsg}[/spoiler]" +
            $"[br]Big Wins[br][spoiler]{bigWinsMsg}[/spoiler]", 
            true, autoDeleteAfter: TimeSpan.FromSeconds(30));
    }
}
