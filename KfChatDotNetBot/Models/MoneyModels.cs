using System.ComponentModel;

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

public class KasinoEventModel
{
    /// <summary>
    /// Text summary of the event itself such as: "How long until the chase ends?" or "Parole granted?"
    /// </summary>
    public required string EventText { get; set; }
    /// <summary>
    /// Unique reference used for tying this event to wagers gamblers are placing
    /// </summary>
    public required string EventId { get; set; }
    /// <summary>
    /// The set of options available for gamblers to select. This is only applicable to WinLose-type bets
    /// It'll be an empty array for closest to finish
    /// </summary>
    public required List<KasinoEventOptionModel> Options { get; set; } = [];
    /// <summary>
    /// The type of event this is, which is important for the purposes of calculating the payout correctly
    /// </summary>
    public required KasinoEventType EventType { get; set; }
    /// <summary>
    /// Timestamp of when the announcement message was received by the client for the purposes of calculating selection
    /// delay. This value is null when the message hasn't been seen yet (either event not started or message lost.
    /// Do not accept wagers where this is null.
    /// </summary>
    public DateTimeOffset? EventAnnouncementReceived { get; set; } = null;
    /// <summary>
    /// State of the kasino event
    /// </summary>
    public required KasinoEventState EventState { get; set; } = KasinoEventState.Incomplete;
    /// <summary>
    /// Whether the payout is weighted based on the selection delay
    /// </summary>
    public required bool SelectionTimeWeightedPayout { get; set; } = false;
}

public class KasinoEventOptionModel
{
    /// <summary>
    /// Unique reference used for tying a gambler's selection to a given option
    /// </summary>
    public required string OptionId { get; set; }
    /// <summary>
    /// Text to describe the option that users are picking
    /// </summary>
    public required string OptionText { get; set; }
    /// <summary>
    /// Whether this option won or not, null while incomplete
    /// </summary>
    public bool? Won { get; set; } = null;
}

public enum KasinoEventType
{
    [Description("Win/Lose")]
    WinLose,
    [Description("Closest to prediction")]
    Prediction,
}

public enum KasinoEventState
{
    /// <summary>
    /// Event still under construction. This is the initial state when an admin creates an event but hasn't yet launched it
    /// </summary>
    Incomplete,
    /// <summary>
    /// Event has been launched but the announcement message hasn't yet been acknowledged by Sneedchat
    /// No bets will be processed until the message is seen
    /// If the message is ultimately lost, the event will never launch
    /// </summary>
    PendingAnnouncement,
    /// <summary>
    /// The event announcement message was seen, the event has started and wagers can now be placed
    /// </summary>
    Started,
    /// <summary>
    /// Closed to new wagers but the event is still ongoing
    /// </summary>
    Closed,
    /// <summary>
    /// Event has closed, it's so over.
    /// </summary>
    Over,
    /// <summary>
    /// Event was abandoned and all wagers canceled
    /// </summary>
    Abandoned
}