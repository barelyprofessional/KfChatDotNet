using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;

namespace KfChatDotNetBot.Extensions;

public static class MoneyExtensions
{
    /// <summary>
    /// Retrieve a gambler entity for a given user
    /// Returns null if createIfNoneExists is false and no gambler exists
    /// Also returns null if the user was permanently banned from gambling
    /// If there are multiple "active" gamblers, only the newest is returned
    /// </summary>
    /// <param name="user">User whose gambler entity you wish to retrieve</param>
    /// <param name="createIfNoneExists">Whether to create a gambler entity if none exists already</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerDbModel?> GetGamblerEntity(this UserDbModel user, bool createIfNoneExists = true, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        // Refetch user as I'm fairly certain some of the buggy behavior is coming from db.Attach being weird
        user = await db.Users.FirstAsync(u => u.Id == user.Id, cancellationToken: ct);
        var gambler =
            await db.Gamblers.OrderBy(x => x.Id).LastOrDefaultAsync(g => g.User == user && g.State != GamblerState.PermanentlyBanned,
                cancellationToken: ct);
        if (!createIfNoneExists) return gambler;
        var permaBanned = await db.Gamblers.AnyAsync(g => g.User == user && g.State == GamblerState.PermanentlyBanned, cancellationToken: ct);
        if (permaBanned) return null;
        var initialBalance = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.MoneyInitialBalance)).ToType<decimal>();
        await db.Gamblers.AddAsync(new GamblerDbModel
        {
            User = user,
            Balance = initialBalance,
            Created = DateTimeOffset.UtcNow,
            RandomSeed = Guid.NewGuid().ToString(),
            State = GamblerState.Active,
            TotalWagered = 0,
            NextVipLevelWagerRequirement = Money.VipLevels[0].BaseWagerRequirement
        }, ct);
        await db.SaveChangesAsync(ct);
        return await db.Gamblers.OrderBy(x => x.Id).LastOrDefaultAsync(g => g.User == user, cancellationToken: ct);
    }

    /// <summary>
    /// Simple check to see whether a user has been permanently banned from the kasino
    /// </summary>
    /// <param name="user">User to check for the permaban</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<bool> IsPermanentlyBanned(this UserDbModel user, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        return await db.Gamblers.AnyAsync(u => u.User.Id == user.Id && u.State == GamblerState.PermanentlyBanned,
            cancellationToken: ct);
    }

    /// <summary>
    /// Modify a gambler's balance by a given +/- amount
    /// </summary>
    /// <param name="gambler">Gambler entity whose balance you wish to modify</param>
    /// <param name="effect">The 'effect' of this modification, as in how much to add or remove</param>
    /// <param name="eventSource">The event which initiated this balance modification</param>
    /// <param name="comment">Optional comment to provide for the transaction</param>
    /// <param name="from">If applicable, who sent the transaction (e.g. if a juicer)</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task ModifyBalance(this GamblerDbModel gambler, decimal effect,
        TransactionSourceEventType eventSource, string? comment = null, GamblerDbModel? from = null,
        CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        gambler = await db.Gamblers.FirstAsync(g => g.Id == gambler.Id, cancellationToken: ct);
        gambler.Balance += effect;
        await db.Transactions.AddAsync(new TransactionDbModel
        {
            Gambler = gambler,
            EventSource = eventSource,
            Effect = effect,
            Time = DateTimeOffset.UtcNow,
            Comment = comment,
            From = from,
            NewBalance = gambler.Balance,
            TimeUnixEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Add a wager to the database
    /// Will also issue a balance update unless you explicitly disable autoModifyBalance
    /// </summary>
    /// <param name="gambler">Gambler who wagered</param>
    /// <param name="wagerAmount">The amount they wagered</param>
    /// <param name="wagerEffect">The effect of the wager on the gambler's balance.
    /// Please note this includes the wager itself. So for a bet of 500 that paid out 50, you pass in an effect of -450
    /// If instead they won 600 then the effect would be +100. the wagered amount is not factored into balance changes,
    /// it's just recorded for calculating bonuses and statistics</param>
    /// <param name="game">The game which was played as part of this wager</param>
    /// <param name="autoModifyBalance">Whether tu automatically update the user's balance according to the wager effect.
    /// Typically you should leave this on so as to ensure every wager has an associated transaction.</param>
    /// <param name="gameMeta">Optionally store metadata related to the wager, such as player choices, or game outcomes.
    /// Data will be serialized to JSON.</param>
    /// <param name="isComplete">Whether the game is 'complete'. Set to false for wagers with unknown outcomes.
    /// NOTE: wagerEffect will be ignored, instead value will be derived from the wagerAmount</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task NewWager(this GamblerDbModel gambler, decimal wagerAmount, decimal wagerEffect,
        WagerGame game, bool autoModifyBalance = true, dynamic? gameMeta = null, bool isComplete = true,
        CancellationToken ct = default)
    {
        var logger = LogManager.GetCurrentClassLogger();
        await using var db = new ApplicationDbContext();
        gambler = await db.Gamblers.FirstAsync(g => g.Id == gambler.Id, cancellationToken: ct);
        string? metaJson = null;
        if (gameMeta != null)
        {
            metaJson = JsonConvert.SerializeObject(gameMeta, Formatting.Indented);
            logger.Debug("Serialized metadata follows");
            logger.Debug(metaJson);
        }

        if (!isComplete)
        {
            wagerEffect = -wagerAmount;
            logger.Debug($"isComplete is false, set wagerEffect to {wagerEffect}");
        }
        decimal multi = 0;
        if (isComplete && wagerAmount > 0 && wagerEffect > 0 && wagerAmount + wagerEffect > 0)
        {
            multi = (wagerAmount + wagerEffect) / wagerAmount;
            logger.Debug($"multi is {multi}");
        }

        gambler.TotalWagered += wagerAmount;
        var wager = await db.Wagers.AddAsync(new WagerDbModel
        {
            Gambler = gambler,
            Time = DateTimeOffset.UtcNow,
            WagerAmount = wagerAmount,
            WagerEffect = wagerEffect,
            Multiplier = multi,
            Game = game,
            GameMeta = metaJson,
            IsComplete = isComplete,
            TimeUnixEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, ct);
        await db.SaveChangesAsync(ct);
        gambler.Balance += wagerEffect;
        if (!autoModifyBalance) return;

        await db.Transactions.AddAsync(new TransactionDbModel
        {
            Gambler = gambler,
            EventSource = TransactionSourceEventType.Gambling,
            Time = DateTimeOffset.UtcNow,
            Effect = wagerEffect,
            Comment = $"Win from wager {wager.Entity.Id}",
            From = null,
            NewBalance = gambler.Balance,
            TimeUnixEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get an active exclusion, returns null if there's no active exclusion
    /// If there's somehow multiple exclusions, will just grab the most recent one
    /// </summary>
    /// <param name="gambler">Gambler entity to retrieve the exclusion for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerExclusionDbModel?> GetActiveExclusion(this GamblerDbModel gambler, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        return (await db.Exclusions.Where(g => g.Gambler.Id == gambler.Id).ToListAsync(ct))
            .LastOrDefault(e => e.Expires <= DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Get random number using the gambler's seed and a given number of iterations
    /// </summary>
    /// <param name="gambler">Gambler entity to reference their random seed</param>
    /// <param name="min">Minimum value for generating random</param>
    /// <param name="max">Maximum value for random, incremented by 1 if add1ToMaxParam is true
    /// so it's consistent with the behavior of min</param>
    /// <param name="iterations">Number of random number generator iterations to run before returning a result</param>
    /// <param name="incrementMaxParam">Increments the 'max' param by 1 as otherwise the value will never be returned by Random.Next()
    /// This is because the default behavior of .NET is unintuitive, min value can be returned but max is never by default</param>
    /// <returns>A random number based on the given parameters</returns>
    /// <exception cref="ArgumentException"></exception>
    public static int GetRandomNumber(this GamblerDbModel gambler, int min, int max, int iterations = 10,
        bool incrementMaxParam = true)
    {
        var random = new Random(gambler.RandomSeed.GetHashCode());
        var result = 0;
        var i = 0;
        if (incrementMaxParam) max++;
        if (iterations <= 0) throw new ArgumentException("Iterations cannot be 0 or lower");
        while (i < iterations)
        {
            i++;
            result = random.Next(min, max);
        }

        return result;
    }

    /// <summary>
    /// Get the user's current VIP level
    /// </summary>
    /// <param name="gambler">Gambler entity whose VIP level you want to get</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerPerkDbModel?> GetVipLevel(this GamblerDbModel gambler, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var perk = await db.Perks.OrderBy(x => x.Id).LastOrDefaultAsync(
            p => p.Gambler.Id == gambler.Id && p.PerkType == GamblerPerkType.VipLevel, ct);
        return perk;
    }

    /// <summary>
    /// Upgrade to the given VIP level. Grants a bonus as part of the level up.
    /// </summary>
    /// <param name="gambler">The gambler you wish to level up</param>
    /// <param name="nextVipLevel">VIP level to grant them</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The bonus they received</returns>
    public static async Task<decimal> UpgradeVipLevel(this GamblerDbModel gambler, NextVipLevelModel nextVipLevel,
        CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        gambler = await db.Gamblers.FirstAsync(g => g.Id == gambler.Id, cancellationToken: ct);
        var payout = nextVipLevel.VipLevel.BonusPayout;
        if (nextVipLevel.Tier > 1)
        {
            payout = nextVipLevel.VipLevel.BonusPayout / (nextVipLevel.VipLevel.Tiers - 1);
        }

        await db.Perks.AddAsync(new GamblerPerkDbModel
        {
            Gambler = gambler,
            PerkType = GamblerPerkType.VipLevel,
            PerkName = nextVipLevel.VipLevel.Name,
            PerkTier = nextVipLevel.Tier,
            Time = DateTimeOffset.UtcNow,
            Payout = payout,
        }, ct);
        gambler.NextVipLevelWagerRequirement = nextVipLevel.WagerRequirement;
        await db.SaveChangesAsync(ct);
        await gambler.ModifyBalance(payout, TransactionSourceEventType.Bonus,
            $"VIP Level '{nextVipLevel.VipLevel.Icon} {nextVipLevel.VipLevel.Name}' Tier {nextVipLevel.Tier} level up bonus", ct: ct);
        return payout;
    }

    /// <summary>
    /// Format an amount of money using configured symbols
    /// </summary>
    /// <param name="amount">The amount you wish to format</param>
    /// <param name="suffixSymbol">Whether to suffix the symbol</param>
    /// <param name="prefixSymbol">Whether to prefix the symbol</param>
    /// <param name="wrapInPlainBbCode">Whether to wrap the resulting string in [plain][/plain] BBCode to avoid characters being interpreted as emotes</param>
    /// <returns></returns>
    public static async Task<string> FormatKasinoCurrencyAsync(this decimal amount, bool suffixSymbol = true,
        bool prefixSymbol = false, bool wrapInPlainBbCode = true)
    {
        var settings = await
            SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.MoneySymbolPrefix, BuiltIn.Keys.MoneySymbolSuffix]);
        var result = string.Empty;
        if (wrapInPlainBbCode)
        {
            result = "[plain]";
        }

        if (prefixSymbol)
        {
            result += settings[BuiltIn.Keys.MoneySymbolPrefix].Value;
        }

        result += $"{amount:N2}";

        if (suffixSymbol)
        {
            result += $" {settings[BuiltIn.Keys.MoneySymbolSuffix].Value}";
        }

        if (wrapInPlainBbCode)
        {
            result += "[/plain]";
        }

        return result;
    }
}
