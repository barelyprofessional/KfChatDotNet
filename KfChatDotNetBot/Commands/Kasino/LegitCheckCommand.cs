using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands.Kasino;

/// <summary>
/// Command to check a user's kasino "legitimacy" by calculating their Return-to-Player (RTP) statistics.
/// RTP represents the percentage of wagered money returned to the player over time.
/// An RTP of 100% means break-even, above 100% means profit, below means loss.
/// </summary>
[KasinoCommand]
public class LegitCheckCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^legitcheck (?<user_id>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^legitcheck (?<user_id>\d+) all$", RegexOptions.IgnoreCase),
        new Regex(@"^legitcheck @(?<username>.+) all$", RegexOptions.IgnoreCase),
        new Regex(@"^legitcheck @(?<username>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^legitcheck$", RegexOptions.IgnoreCase),
        new Regex(@"^legitcheck all$", RegexOptions.IgnoreCase),
    ];

    public string? HelpText => "Check a user's kasino RTP statistics";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(30)
    };

    // Minimum wagers required for a game to be considered for "luckiest game"
    // This prevents small sample sizes from skewing results (e.g., 1 win on 1 bet = 200% RTP)
    private const int MinWagersForLuckiestGame = 10;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();

        var isAll = message.MessageRaw.EndsWith(" all");

        UserDbModel? targetUser;
        if (arguments["username"].Success)
        {
            var chatUser = botInstance.FindUserByName(arguments["username"].Value);
            if (chatUser == null)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, couldn't find that user in chat. They must be present in chat to look up by username.",
                    true);
                return;
            }
            targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == chatUser.Id, ctx);
        }
        else if (arguments["user_id"].Success)
        {
            var targetUserId = int.Parse(arguments["user_id"].Value);
            targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == targetUserId, ctx);
        }
        else
        {
            targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == user.KfId, ctx);
        }

        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, that user doesn't exist.",
                true);
            return;
        }

        // A user can have multiple gambler entities (e.g., if they abandoned an account or got reset).
        // We want to aggregate stats across ALL their gambler entities for a complete picture.
        List<int> gamblerIds = [];
        if (isAll)
        {
            gamblerIds = await db.Gamblers
                .Where(g => g.User.Id == targetUser.Id)
                .Select(g => g.Id)
                .ToListAsync(ctx);
        }
        else
        {
            var gambler = await Money.GetGamblerEntityAsync(targetUser.Id, ct: ctx);
            if (gambler == null)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, {targetUser.KfUsername} has never played at the kasino.", true);
                return;
            }
            gamblerIds.Add(gambler.Id);
        }


        if (gamblerIds.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, {targetUser.KfUsername} has never played at the kasino.", true);
            return;
        }

        // Only include completed wagers - incomplete ones are pending bets (e.g., event outcomes)
        var wagers = await db.Wagers
            .Where(w => gamblerIds.Contains(w.Gambler.Id) && w.IsComplete)
            .ToListAsync(ctx);

        if (wagers.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, {targetUser.KfUsername} has no completed kasino wagers on record.", true);
            return;
        }

        // RTP Calculation:
        // - WagerAmount: The amount the user bet
        // - WagerEffect: The net change to balance (negative for loss, positive for profit)
        // - TotalReturned = WagerAmount + WagerEffect (what they got back)
        //   Example: Bet 100, lost -> WagerEffect = -100, Returned = 100 + (-100) = 0
        //   Example: Bet 100, won 150 total -> WagerEffect = +50 (profit), Returned = 100 + 50 = 150
        // - RTP% = (TotalReturned / TotalWagered) * 100
        var totalWagered = wagers.Sum(w => w.WagerAmount);
        var totalReturned = wagers.Sum(w => w.WagerAmount + w.WagerEffect);
        var overallRtp = totalWagered > 0 ? (totalReturned / totalWagered) * 100 : 0;

        // Group wagers by game type to find per-game statistics
        var gameStats = wagers
            .GroupBy(w => w.Game)
            .Select(g => new GameStatistic
            {
                Game = g.Key,
                WagerCount = g.Count(),
                TotalWagered = g.Sum(w => w.WagerAmount),
                TotalReturned = g.Sum(w => w.WagerAmount + w.WagerEffect)
            })
            .ToList();

        // Calculate RTP for each game (done separately to avoid division in the LINQ projection)
        foreach (var stat in gameStats)
        {
            stat.Rtp = stat.TotalWagered > 0 ? (stat.TotalReturned / stat.TotalWagered) * 100 : 0;
        }

        // Find the game with highest RTP, but only if they have enough wagers to be statistically meaningful
        var luckiestGame = gameStats
            .Where(g => g.WagerCount >= MinWagersForLuckiestGame)
            .MaxBy(g => g.Rtp);

        // Build response
        var response =
            $"{user.FormatUsername()}, {targetUser.KfUsername} RTP: {overallRtp:F2}% | " +
            $"Wagered: {await totalWagered.FormatKasinoCurrencyAsync()} | " +
            $"Returned: {await totalReturned.FormatKasinoCurrencyAsync()} | " +
            $"Wagers: {wagers.Count:N0}";

        if (luckiestGame != null)
        {
            var gameName = luckiestGame.Game.Humanize();
            response +=
                $" | Luckiest: {gameName} ({luckiestGame.Rtp:F2}% RTP, {luckiestGame.WagerCount:N0} wagers, {await luckiestGame.TotalWagered.FormatKasinoCurrencyAsync()} wagered)";
        }

        await botInstance.SendChatMessageAsync(response, true);
    }

    /// <summary>
    /// Helper class to hold per-game statistics during calculation.
    /// </summary>
    private class GameStatistic
    {
        public WagerGame Game { get; init; }
        public int WagerCount { get; init; }
        public decimal TotalWagered { get; init; }
        public decimal TotalReturned { get; init; }
        public decimal Rtp { get; set; }
    }
}
