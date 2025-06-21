using System.ComponentModel;

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
    public required string RandomSeed { get; set; }
    /// <summary>
    /// When the gambler entity was created
    /// </summary>
    public required DateTimeOffset Created { get; set; }
}

public class TransactionDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// User whose balance was affected by this transaction
    /// </summary>
    public required GamblerDbModel User { get; set; }
    /// <summary>
    /// Source of the transaction event
    /// </summary>
    public required TransactionSourceEventType EventSource { get; set; }
    /// <summary>
    /// Time when the event occurred
    /// </summary>
    public required DateTimeOffset Time { get; set; }
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
}

public class WagerDbModel
{
    /// <summary>
    /// ID fo the database row
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// User who wagered
    /// </summary>
    public required GamblerDbModel User { get; set; }
    /// <summary>
    /// Time they wagered
    /// </summary>
    public required DateTimeOffset Time { get; set; }
    /// <summary>
    /// Amount the user wagered
    /// </summary>
    public required decimal WagerAmount { get; set; }
    /// <summary>
    /// Effect of the wager on the user's balance
    /// </summary>
    public required decimal WagerEffect { get; set; }
    /// <summary>
    /// Game they played to wager the amount (Note: enum must be extended for any new games.
    /// Don't remove games which are legacy from the enum, just give them the Obsolete attribute)
    /// </summary>
    public required WagerGame Game { get; set; }
    /// <summary>
    /// Multiplier if applicable. 0 if it was a complete loss
    /// </summary>
    public required decimal Multiplier { get; set; }
    /// <summary>
    /// An optional field to store serialized information about the game that was played
    /// </summary>
    public string? GameMeta { get; set; } = null;
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
    /// Some type of bonus, like rakeback or a reload. Do not use for hostess rewards
    /// </summary>
    Bonus,
    /// <summary>
    /// Use this only for hostess juicers as the sum of these juicers in a given day can influence the hostess' behavior 
    /// </summary>
    Hostess
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
    CoinFlip
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
    PermanentlyBanned
}