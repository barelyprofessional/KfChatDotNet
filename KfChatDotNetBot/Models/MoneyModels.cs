using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Models;

public class MoneyVipLevel
{
    /// <summary>
    /// Name of the VIP level
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Number of tiers the VIP level has
    /// Steps between VIP tiers are calculated by comparing with the next VIP level and dividing by number of tiers
    /// e.g. (100,000 - 10,000) / 5 = 18,000 steps
    /// Tier 1 = 10,000
    /// Tier 2 = 28,000
    /// Tier 3 = 46,000
    /// Tier 4 = 64,000
    /// Tier 5 = 84,000
    /// Next VIP level at 100,000
    /// What happens if they're at the last VIP level? They remain stuck at tier 1 forever regardless of this value
    /// This is really just so that we have flexibility to add further tiers later without messing anything up
    /// since there's no telling how easy it will be to attain the high levels at this point
    /// </summary>
    public required int Tiers { get; set; }
    /// <summary>
    /// Icon to display next to the name, like an emoji diamond or a small image embedded with bbcode [img] tags
    /// </summary>
    public required string Icon { get; set; }
    /// <summary>
    /// The wager requirement for this level. This is the requirement for the base (tier 1) level
    /// Remaining tiers are calculated based on the wager requirement for the next tier
    /// </summary>
    public required decimal BaseWagerRequirement { get; set; }
    /// <summary>
    /// Payout when you attain this level.
    /// Tiers (beyond 1) pay out: BonusPayout / (Tiers - 1) (e.g. 1,000 / 4 = 250 for tier 2-5
    /// </summary>
    public required decimal BonusPayout { get; set; }
}

public class NextVipLevelModel
{
    /// <summary>
    /// The VIP level that's coming up next.
    /// Could be the same as the existing level if it's just the next tier.
    /// </summary>
    public required MoneyVipLevel VipLevel { get; set; }
    /// <summary>
    /// What tier this is for
    /// </summary>
    public required int Tier { get; set; }
    /// <summary>
    /// The wager requirement to reach this tier that factors in the tier
    /// </summary>
    public required decimal WagerRequirement { get; set; }
}