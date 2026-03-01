using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace KfChatDotNetBot.Models.DbModels;

public class GamblerDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// User that this gambler entity is associated with.
    /// A user can have multiple associated gambler entities, but only one should be active
    /// </summary>
    public required UserDbModel User { get; set; }
    /// <summary>
    /// Gambler's balance. It can be negative if an admin has forced them into an overdraft
    /// Values are fractional, it is NOT stored as cents, therefore 100.00 KKK is stored as "100" in the database
    /// </summary>
    public required decimal Balance { get; set; }
    /// <summary>
    /// What state the gambler entity is in
    /// </summary>
    public required GamblerState State { get; set; } = GamblerState.Active;
    /// <summary>
    /// The seed value given to any instance of Random that's associated with the gambler
    /// </summary>
    [MaxLength(256)]
    public required string RandomSeed { get; set; }
    /// <summary>
    /// When the gambler entity was created
    /// </summary>
    public required DateTimeOffset Created { get; set; }
    /// <summary>
    /// Reference value for total wagered during the entity's lifetime
    /// This value is recalculated whenever the bot restarts to ensure integrity
    /// </summary>
    public required decimal TotalWagered { get; set; }
    /// <summary>
    /// Wager requirement for the next VIP level
    /// If TotalWagered reaches this value, it'll trigger the calculation
    /// </summary>
    public required decimal NextVipLevelWagerRequirement { get; set; }
}

public class TransactionDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Gambler whose balance was affected by this transaction
    /// </summary>
    public required GamblerDbModel Gambler { get; set; }
    /// <summary>
    /// Source of the transaction event
    /// </summary>
    public required TransactionSourceEventType EventSource { get; set; }
    /// <summary>
    /// Time when the event occurred
    /// </summary>
    public required DateTimeOffset Time { get; set; }
    /// <summary>
    /// Time represented as a 64-bit UNIX epoch
    /// This just exists to make it far more efficient to query a range of txns
    /// as then we can use native SQLite dialect to select e.g. last 24 hours
    /// instead of copying thousands of rows into memory and using LINQ
    /// </summary>
    public required long TimeUnixEpochSeconds { get; set; }
    /// <summary>
    /// Effect of the transaction, plus or minus
    /// </summary>
    public required decimal Effect { get; set; }
    /// <summary>
    /// Optional descriptive comment for the transaction. e.g. "Win from wager [id]", "Balance adjustment by Avenue", "Juicer from Null", etc.
    /// </summary>
    public string? Comment { get; set; } = null;
    /// <summary>
    /// Sender of the transaction in the case of a juicer, null otherwise
    /// </summary>
    public GamblerDbModel? From { get; set; } = null;
    /// <summary>
    /// Snapshot of the gambler's balance after this transaction's effect was applied
    /// </summary>
    public required decimal NewBalance { get; set; }
}

public class WagerDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Gambler who wagered
    /// </summary>
    public required GamblerDbModel Gambler { get; set; }
    /// <summary>
    /// Time they wagered
    /// </summary>
    public required DateTimeOffset Time { get; set; }
    /// <summary>
    /// Time represented as a 64-bit UNIX epoch
    /// This just exists to make it far more efficient to query a range of wagers
    /// as then we can use native SQLite dialect to select e.g. last 24 hours
    /// instead of copying thousands of rows into memory and using LINQ
    /// </summary>
    public required long TimeUnixEpochSeconds { get; set; }
    /// <summary>
    /// Amount the gambler wagered
    /// </summary>
    public required decimal WagerAmount { get; set; }
    /// <summary>
    /// Effect of the wager on the gambler's balance
    /// </summary>
    public required decimal WagerEffect { get; set; }
    /// <summary>
    /// Game they played to wager the amount (Note: enum must be extended for any new games.
    /// Don't remove games which are legacy from the enum, just give them the Obsolete attribute)
    /// </summary>
    public required WagerGame Game { get; set; }
    /// <summary>
    /// Multiplier, e.g. 10.5x if a $1 wager paid out $10.50. 0 if it was a complete loss
    /// </summary>
    public required decimal Multiplier { get; set; }
    /// <summary>
    /// An optional field to store serialized information about the game that was played
    /// </summary>
    public string? GameMeta { get; set; } = null;
    /// <summary>
    /// Whether the results of the wager have been realized yet (i.e., is the game 'complete'?)
    /// This is useful for wagers related to bets on the outcome of events
    /// For incomplete bets: set the effect to -wager, subtract it from the user's balance, generate a txn for the wager
    /// Then when the outcome of the bet is fully realized, modify the effect accordingly, generate a new txn for the
    /// payout and set a multiplier based on the win (if any)
    /// </summary>
    public required bool IsComplete { get; set; }
}

public class GamblerExclusionDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Gambler who is excluded
    /// </summary>
    public required GamblerDbModel Gambler { get; set; }
    /// <summary>
    /// When the exclusion expires
    /// </summary>
    public required DateTimeOffset Expires { get; set; }
    /// <summary>
    /// When the exclusion was created / began
    /// </summary>
    public required DateTimeOffset Created { get; set; }
    /// <summary>
    /// What triggered the exclusion
    /// </summary>
    public required ExclusionSource Source { get; set; }
}

public class GamblerPerkDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// Gambler entity the perk is associated with
    /// </summary>
    public required GamblerDbModel Gambler { get; set; }
    /// <summary>
    /// Name of the perk
    /// </summary>
    [MaxLength(256)]
    public required string PerkName { get; set; }
    /// <summary>
    /// Time when the perk was attained
    /// </summary>
    public required DateTimeOffset Time { get; set; }
    /// <summary>
    /// Optional metadata associated with the perk
    /// </summary>
    public string? Metadata { get; set; } = null;
    /// <summary>
    /// What type of perk is this
    /// </summary>
    public required GamblerPerkType PerkType { get; set; }
    /// <summary>
    /// The tier the perk is at.
    /// If tiers are not applicable, set to null
    /// </summary>
    public int? PerkTier { get; set; }
    /// <summary>
    /// The payout from this perk, if any. If none, set to null
    /// </summary>
    public decimal? Payout { get; set; }
}

public enum GamblerPerkType
{
    /// <summary>
    /// For literally anything else, though you should probably just extend this enum
    /// </summary>
    Other = -1,
    /// <summary>
    /// Used for tracking VIP levels attained
    /// </summary>
    [Description("VIP Level")]
    VipLevel
}

public enum ExclusionSource
{
    /// <summary>
    /// Exclusion as a result of the hostess' action
    /// </summary>
    Hostess,
    /// <summary>
    /// Exclusions placed by administrators
    /// </summary>
    Administrative
}

/// <summary>
/// What event triggered this transaction
/// </summary>
public enum TransactionSourceEventType
{
    /// <summary>
    /// Generic catch-all type if nothing else suits
    /// </summary>
    Other,
    /// <summary>
    /// Juice from another user. This is only for person to person transactions, use Bonus for kasino rewards
    /// </summary>
    Juicer,
    /// <summary>
    /// Transaction generated from the result of a wager
    /// </summary>
    Gambling,
    /// <summary>
    /// For recording events related to an administrative action. e.g. balance adjustments
    /// </summary>
    Administrative,
    /// <summary>
    /// Some type of bonus, like a VIP level up. Rakeback / reloads have separate enums for this
    /// </summary>
    Bonus,
    /// <summary>
    /// Specifically use for rakeback as we use the delta between last rakeback txn to calculate total wagered
    /// to figure out what the next rakeback should be (if they've wagered enough to be eligible for one)
    /// </summary>
    Rakeback,
    /// <summary>
    /// Use specifically for daily reloads as we use the timing of the last reload txn to figure out if the most
    /// recent reload has been claimed yet or not
    /// </summary>
    Reload,
    /// <summary>
    /// Use this only for hostess juicers as the sum of these juicers in a given day can influence the hostess' behavior 
    /// </summary>
    Hostess,
    /// <summary>
    /// Specifically use for lossback as we use the delta between last lossback txn to calculate total lost
    /// to figure out what the next lossback should be. (Basically return a small % of the player's losses
    /// unless the player's actual position is positive during the period, then tell them to fuck off)
    /// </summary>
    Lossback,
    /// <summary>
    /// A specific form of 24 hour time-based reload that has no wager requirement
    /// </summary>
    DailyDollar,
    ///<summary>
    ///A form of juicer where the value is split among a number of participants
    /// </summary>
    Rain,
    Deposit,
    Withdraw,
    Sponsorship,
    Loan
}

public enum WagerGame
{
    Limbo,
    Dice,
    Mines,
    Planes,
    [Description("Lambchop")]
    LambChop,
    Keno,
    [Description("Coinflip")]
    CoinFlip,
    /// <summary>
    /// This is for betting pools based on some sort of event or outcome
    /// </summary>
    Event,
    [Description("Guess what number I'm thinking of")]
    GuessWhatNumber,
    Wheel,
    Slots,
    Blackjack,
    [Description("Plinko")]
    Plinko,
    [Description("Roulette but live")]
    Roulette
}

public enum GamblerState
{
    /// <summary>
    /// Gambler entity is active and user can wager using the profile
    /// </summary>
    Active,
    /// <summary>
    /// Gambler entity has been disabled by an administrator (e.g. due to cheating)
    /// The user will get a new gambler entity when they next interact with the kasino
    /// </summary>
    AdministrativelyDisabled,
    /// <summary>
    /// Gambler entity that was abandoned by the user (e.g. to escape an exclusion or crippling debt)
    /// </summary>
    Abandoned,
    /// <summary>
    /// Entity was permanently banned. This will prevent future gambler entities being created for this user
    /// and will effectively lock them out of the game entirely
    /// </summary>
    PermanentlyBanned,
    /// <summary>
    /// Gambler rendered inactive by the End of Year 2025 Great Reset
    /// This is treated no different to abandonment, state exists for
    /// the purposes of tracking statistics later to see how much KKK
    /// was erased by this event
    /// </summary>
    EndOfYear2025Liquidated
}
