using KfChatDotNetBot.Models;

namespace KfChatDotNetBot.Services;

public static class Money
{
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
}