using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Services;

public static class Money
{
    private static Logger _logger = LogManager.GetCurrentClassLogger();
    /// <summary>
    /// This is the list of available VIP levels for gamblers to ascend
    /// The order of this array is important, it begins with the loest level VIP, and ascends IN ORDER to the max level
    /// </summary>
    public static List<MoneyVipLevel> VipLevels =
    [
        new MoneyVipLevel
        {
            Name = "Rip City",
            Tiers = 5,
            Icon = ":ross:",
            BaseWagerRequirement = 10_000,
            BonusPayout = 250
        },
        new MoneyVipLevel
        {
            Name = "Chinesium",
            Tiers = 5,
            Icon = ":gold:",
            BaseWagerRequirement = 100_000,
            BonusPayout = 500
        },
        new MoneyVipLevel
        {
            Name = "Juice Fiend",
            Tiers = 5,
            Icon = ":juice:",
            BaseWagerRequirement = 1_000_000,
            BonusPayout = 1000
        },
        new MoneyVipLevel
        {
            Name = "Unemployment Line",
            Tiers = 5,
            Icon = ":tugboat:",
            BaseWagerRequirement = 5_000_000,
            BonusPayout = 2500
        },
        new MoneyVipLevel
        {
            Name = "Down Immensely",
            Tiers = 5,
            Icon = ":felted:",
            BaseWagerRequirement = 10_000_000,
            BonusPayout = 5000
        },
        new MoneyVipLevel
        {
            Name = "Glutton for Punishment",
            Tiers = 5,
            Icon = ":ow:",
            BaseWagerRequirement = 25_000_000,
            BonusPayout = 7500
        },
        new MoneyVipLevel
        {
            Name = "Targeted by Evil Eddie",
            Tiers = 5,
            Icon = ":bogged:",
            BaseWagerRequirement = 50_000_000,
            BonusPayout = 10_000
        },
        new MoneyVipLevel
        {
            Name = "Epic High Roller",
            Tiers = 5,
            Icon = ":wow:",
            BaseWagerRequirement = 100_000_000,
            BonusPayout = 15_000
        },
        new MoneyVipLevel
        {
            Name = "99% of Gamblers",
            Tiers = 5,
            Icon = ":wall:",
            BaseWagerRequirement = 500_000_000,
            BonusPayout = 25_000
        },
        new MoneyVipLevel
        {
            Name = "Billionaire Club",
            Tiers = 5,
            Icon = ":drink:",
            BaseWagerRequirement = 1_000_000_000,
            BonusPayout = 50_000
        },
        new MoneyVipLevel
        {
            Name = "Upfag",
            Tiers = 5,
            Icon = ":gay:",
            BaseWagerRequirement = 5_000_000_000,
            BonusPayout = 75_000
        },
        new MoneyVipLevel
        {
            Name = "No Regrets",
            Tiers = 5,
            Icon = ":woo:",
            BaseWagerRequirement = 50_000_000_000,
            BonusPayout = 100_000
        },
        new MoneyVipLevel
        {
            Name = "A Small Juicer of a Million Dollars",
            Tiers = 5,
            Icon = ":trump:",
            BaseWagerRequirement = 250_000_000_000,
            BonusPayout = 1_000_000
        },
        new MoneyVipLevel
        {
            Name = "Wannabe Bossman",
            Tiers = 5,
            Icon = ":lossmanjack:",
            BaseWagerRequirement = 500_000_000_000,
            BonusPayout = 2_000_000
        },
        new MoneyVipLevel
        {
            Name = "TRILLION DOLLAR COIN FLIP",
            Tiers = 5,
            Icon = ":winner:",
            BaseWagerRequirement = 1_000_000_000_000,
            BonusPayout = 4_000_000
        },
        new MoneyVipLevel
        {
            Name = "Madness",
            Tiers = 5,
            Icon = ":lunacy:",
            BaseWagerRequirement = 5_000_000_000_000,
            BonusPayout = 10_000_000
        },
        new MoneyVipLevel
        {
            Name = "Nowhere to go from here",
            Tiers = 5,
            Icon = ":achievement:",
            BaseWagerRequirement = 50_000_000_000_000,
            // Fuck you pussy
            BonusPayout = 1
        }
    ];

    public static List<decimal> CalculateTiers(MoneyVipLevel vipLevel)
    {
        // The list is in ascending order
        var nextLevel = VipLevels.FirstOrDefault(v => v.BaseWagerRequirement > vipLevel.BaseWagerRequirement);
        // Max level has no tiers
        if (nextLevel == null) return [vipLevel.BaseWagerRequirement];
        var wagerRequirement = vipLevel.BaseWagerRequirement;
        var step = (nextLevel.BaseWagerRequirement - vipLevel.BaseWagerRequirement) / vipLevel.Tiers;
        var tiers = new List<decimal>();
        while (wagerRequirement < nextLevel.BaseWagerRequirement)
        {
            tiers.Add(wagerRequirement);
            wagerRequirement += step;
        }
        return tiers;
    }

    /// <summary>
    /// Get the next VIP level based on the wager amount given
    /// </summary>
    /// <param name="wagered">Wager amount to calculate the next level</param>
    /// <returns>null if the user is at the max level</returns>
    public static NextVipLevelModel? GetNextVipLevel(decimal wagered)
    {
        var level = VipLevels.LastOrDefault(v => v.BaseWagerRequirement < wagered);
        if (level == null) return null;
        var tiers = CalculateTiers(level);
        var nextTier = tiers.FirstOrDefault(t => wagered < t);
        // default(decimal) is 0
        // This happens if the user is between tier 5 and their next level
        if (nextTier == 0)
        {
            var nextLevel = VipLevels[VipLevels.IndexOf(level) + 1];
            return new NextVipLevelModel
            {
                VipLevel = nextLevel,
                Tier = 1,
                WagerRequirement = nextLevel.BaseWagerRequirement
            };
        }
        return new NextVipLevelModel
        {
            VipLevel = level,
            Tier = tiers.IndexOf(nextTier) + 1,
            WagerRequirement = nextTier
        };
    }
    
    /// <summary>
    /// Retrieve a gambler entity for a given user
    /// Returns null if createIfNoneExists is false and no gambler exists
    /// Also returns null if the user was permanently banned from gambling
    /// If there are multiple "active" gamblers, only the newest is returned
    /// </summary>
    /// <param name="userId">User whose gambler entity you wish to retrieve</param>
    /// <param name="createIfNoneExists">Whether to create a gambler entity if none exists already</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerDbModel?> GetGamblerEntityAsync(int userId, bool createIfNoneExists = true, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user == null)
        {
            throw new Exception($"User ID {userId} not found");
        }
        var gambler =
            await db.Gamblers.AsNoTracking().OrderBy(x => x.Id).Include(x => x.User).LastOrDefaultAsync(g => g.User.Id == user.Id && g.State != GamblerState.PermanentlyBanned,
                cancellationToken: ct);
        _logger.Info($"Retrieved entity for {user.KfUsername}. Is Gambler Entity Null? => {gambler == null}");
        if (gambler != null && gambler.State != GamblerState.Abandoned && gambler.State != GamblerState.EndOfYear2025Liquidated)
        {
            _logger.Info($"Gambler entity details: {gambler.Id}, Created: {gambler.Created:o}");
            return gambler;
        }
        if (!createIfNoneExists) return gambler;
        var permaBanned = await IsPermanentlyBannedAsync(userId, ct);
        _logger.Info($"permaBanned => {permaBanned}");
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
        var newEntity = await db.Gamblers.AsNoTracking().OrderBy(x => x.Id).Include(x => x.User)
            .LastOrDefaultAsync(g => g.User == user, cancellationToken: ct);
        if (newEntity == null)
        {
            throw new InvalidOperationException(
                $"Caught a null when trying to retrieve freshly created gambler entity for user {user.KfId}");
        }
        _logger.Info($"New gambler entity created for {user.KfUsername} with ID {newEntity.Id}");
        return newEntity;
    }
        
    /// <summary>
    /// Simple check to see whether a user has been permanently banned from the kasino
    /// </summary>
    /// <param name="userId">User to check for the permaban</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<bool> IsPermanentlyBannedAsync(int userId, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        return await db.Gamblers.AnyAsync(u => u.User.Id == userId && u.State == GamblerState.PermanentlyBanned,
            cancellationToken: ct);
    }
    
    /// <summary>
    /// Modify a gambler's balance by a given +/- amount
    /// </summary>
    /// <param name="gamblerId">Gambler entity whose balance you wish to modify</param>
    /// <param name="effect">The 'effect' of this modification, as in how much to add or remove</param>
    /// <param name="eventSource">The event which initiated this balance modification</param>
    /// <param name="comment">Optional comment to provide for the transaction</param>
    /// <param name="fromId">If applicable, who sent the transaction (e.g. if a juicer)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>New balance after modification</returns>
    public static async Task<decimal> ModifyBalanceAsync(int gamblerId, decimal effect,
        TransactionSourceEventType eventSource, string? comment = null, int? fromId = null,
        CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var gambler = await db.Gamblers.FirstOrDefaultAsync(x => x.Id == gamblerId, cancellationToken: ct);
        if (gambler == null)
        {
            throw new Exception($"Could not find gambler entity with given ID {gamblerId}");
        }
        _logger.Info($"Updating balance for {gambler.Id} with effect {effect:N}. Balance is currently {gambler.Balance:N}");
        gambler.Balance += effect;
        _logger.Info($"Balance is now {gambler.Balance:N}");
        var from = await db.Gamblers.FirstOrDefaultAsync(x => x.Id == fromId, cancellationToken: ct);
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
        return gambler.Balance;
    }
    
    /// <summary>
    /// Add a wager to the database
    /// Will also issue a balance update unless you explicitly disable autoModifyBalance
    /// </summary>
    /// <param name="gamblerId">Gambler who wagered</param>
    /// <param name="wagerAmount">The amount they wagered</param>
    /// <param name="wagerEffect">The effect of the wager on the gambler's balance.
    /// Please note this includes the wager itself. So for a bet of 500 that paid out 50, you pass in an effect of -450
    /// If instead they won 600 then the effect would be +100. the wagered amount is not factored into balance changes,
    /// it's just recorded for calculating bonuses and statistics</param>
    /// <param name="game">The game which was played as part of this wager</param>
    /// <param name="autoModifyBalance">Whether tu automatically update the user's balance according to the wager effect.
    /// Typically, you should leave this on so as to ensure every wager has an associated transaction.</param>
    /// <param name="gameMeta">Optionally store metadata related to the wager, such as player choices, or game outcomes.
    /// Data will be serialized to JSON.</param>
    /// <param name="isComplete">Whether the game is 'complete'. Set to false for wagers with unknown outcomes.
    /// NOTE: wagerEffect will be ignored, instead value will be derived from the wagerAmount</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Returns the gambler's balance</returns>
    public static async Task<decimal> NewWagerAsync(int gamblerId, decimal wagerAmount, decimal wagerEffect,
        WagerGame game, bool autoModifyBalance = true, dynamic? gameMeta = null, bool isComplete = true,
        CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var gambler = await db.Gamblers.Include(gamblerDbModel => gamblerDbModel.User)
            .FirstOrDefaultAsync(x => x.Id == gamblerId, ct);
        if (gambler == null)
        {
            throw new Exception($"Tried to add wager for permanently excluded gambler {gamblerId}");
        }
        _logger.Info($"Adding a wager for {gambler.User.KfUsername}. wagerAmount => {wagerAmount:N}, " +
                     $"wagerEffect => {wagerEffect:N}, game => {game.Humanize()}, autoModifyBalance => {autoModifyBalance}, " +
                     $"isComplete => {isComplete}");
        string? metaJson = null;
        if (gameMeta != null)
        {
            metaJson = JsonConvert.SerializeObject(gameMeta, Formatting.Indented);
            _logger.Info("Serialized metadata follows");
            _logger.Info(metaJson);
        }

        if (!isComplete)
        {
            wagerEffect = -wagerAmount;
            _logger.Info($"isComplete is false, set wagerEffect to {wagerEffect}");
        }
        decimal multi = 0;
        if (isComplete && wagerAmount > 0 && wagerEffect > 0 && wagerAmount + wagerEffect > 0)
        {
            multi = (wagerAmount + wagerEffect) / wagerAmount;
            _logger.Info($"multi is {multi}");
        }

        gambler.TotalWagered += wagerAmount;
        _logger.Info($"Updated TotalWagered to {gambler.TotalWagered}");
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
        _logger.Info($"Added wager row with ID {wager.Entity.Id}");
        if (!autoModifyBalance) return gambler.Balance;
        gambler.Balance += wagerEffect;

        var txn = await db.Transactions.AddAsync(new TransactionDbModel
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
        _logger.Info($"Added transaction with ID {txn.Entity.Id}");
        return gambler.Balance;
    }
    
    /// <summary>
    /// Get an active exclusion, returns null if there's no active exclusion
    /// If there's somehow multiple exclusions, will just grab the most recent one
    /// </summary>
    /// <param name="gamblerId">Gambler ID to retrieve the exclusion for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerExclusionDbModel?> GetActiveExclusionAsync(int gamblerId, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        return (await db.Exclusions.Where(g => g.Gambler.Id == gamblerId).ToListAsync(ct))
            .LastOrDefault(e => e.Expires >= DateTimeOffset.UtcNow);
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
    public static int GetRandomNumber(GamblerDbModel gambler, int min, int max, int iterations = 10,
        bool incrementMaxParam = true)
    {
        var rng = StandardRng.Create();
        var random = RandomShim.Create(rng);
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
    /// Get random number double [0, 1]
    /// </summary>
    /// <param name="gambler">Gambler entity to reference their random seed</param>
    /// <param name="iterations">Number of random number generator iterations to run before returning a result</param>
    /// <returns>A random number based on the given parameters</returns>
    /// <exception cref="ArgumentException"></exception>
    public static double GetRandomDouble(GamblerDbModel gambler, int iterations = 10)
    {
        var rng = StandardRng.Create();
        var random = RandomShim.Create(rng);
        var result = 0.0;
        var i = 0;
        if (iterations <= 0) throw new ArgumentException("Iterations cannot be 0 or lower");
        while (i < iterations)
        {
            i++;
            result = random.NextDouble();
        }
        return result;
    }
    
    /// <summary>
    /// Get the user's current VIP level
    /// </summary>
    /// <param name="gambler">Gambler entity whose VIP level you want to get</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    public static async Task<GamblerPerkDbModel?> GetVipLevelAsync(GamblerDbModel gambler, CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var perk = await db.Perks.OrderBy(x => x.Id).LastOrDefaultAsync(
            p => p.Gambler.Id == gambler.Id && p.PerkType == GamblerPerkType.VipLevel, ct);
        _logger.Info($"User's VIP perk is null? {perk == null}");
        if (perk != null)
        {
            _logger.Info($"VIP perk found for {gambler.User.Id}, {perk.PerkName} tier {perk.PerkTier}");
        }
        return perk;
    }
    
    /// <summary>
    /// Upgrade to the given VIP level. Grants a bonus as part of the level up.
    /// </summary>
    /// <param name="gamblerId">The gambler you wish to level up</param>
    /// <param name="nextVipLevel">VIP level to grant them</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The bonus they received</returns>
    public static async Task<decimal> UpgradeVipLevelAsync(int gamblerId, NextVipLevelModel nextVipLevel,
        CancellationToken ct = default)
    {
        await using var db = new ApplicationDbContext();
        var gambler = await db.Gamblers.FirstOrDefaultAsync(x => x.Id == gamblerId, ct);
        if (gambler == null)
        {
            throw new Exception($"Tried to upgrade VIP level for gambler with ID {gamblerId} who does not exist");
        }
        var payout = nextVipLevel.VipLevel.BonusPayout;
        if (nextVipLevel.Tier > 1)
        {
            payout = nextVipLevel.VipLevel.BonusPayout / (nextVipLevel.VipLevel.Tiers - 1);
        }
        _logger.Info($"Calculated a VIP payout of {payout:N} for gambler ID {gambler.Id}");

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
        await ModifyBalanceAsync(gamblerId, payout, TransactionSourceEventType.Bonus,
            $"VIP Level '{nextVipLevel.VipLevel.Icon} {nextVipLevel.VipLevel.Name}' Tier {nextVipLevel.Tier} level up bonus", ct: ct);
        return payout;
    }
    
    /// <summary>
    /// Generate a short random string based on the first 4 bytes of a GUID for event IDs
    /// </summary>
    /// <returns>Returns a lowercase hex representation of the 4 bytes. e.g. 7ec79eb2</returns>
    public static string GenerateEventId()
    {
        return Convert.ToHexString(Guid.NewGuid().ToByteArray()[..4]).ToLower();
    }

    /// <summary>
    /// Get the current Kasino day based on the configured timezone offset
    /// </summary>
    /// <returns>Kasino day at midnight</returns>
    /// <exception cref="InvalidOperationException">Thrown if Kasino.Timezone is null or empty</exception>
    public static async Task<DateTimeOffset> GetKasinoDate()
    {
        var tz = await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoTimezone);
        if (string.IsNullOrEmpty(tz.Value))
        {
            throw new InvalidOperationException();
        }

        var systemTz = TimeZoneInfo.FindSystemTimeZoneById(tz.Value);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, systemTz);
        return new DateTimeOffset(now.Date, systemTz.BaseUtcOffset);
    }
}