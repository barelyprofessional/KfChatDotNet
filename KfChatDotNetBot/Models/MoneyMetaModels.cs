namespace KfChatDotNetBot.Models;

// Stash all the models used for perk or game metadata here
public class KasinoWagerBaseEventMetaModel
{
    /// <summary>
    /// Event type for this meta model for the purposes of figuring out which model to use when deserializing
    /// </summary>
    public required KasinoEventType EventType { get; set; }
    /// <summary>
    /// Unique reference tracking the shared event ID stored in the settings
    /// </summary>
    public required string SharedEventId { get; set; }
    /// <summary>
    /// How long it took the user to make a selection. This is based on the event announcement msg recv - sent timestamp
    /// that SneedChat provided so the bot won't unfairly penalize users who were delayed by chat lag
    /// </summary>
    public required TimeSpan SelectionDelay { get; set; }
}
/// <summary>
/// Metadata model tracking a gambler's wager information related to win/lose games specifically
/// </summary>
public class KasinoWagerWinLoseEventMetaModel : KasinoWagerBaseEventMetaModel
{
    /// <summary>
    /// Unique reference tracking the option the gambler selected. Tracked as a GUID in case the option text changes
    /// </summary>
    public required string OptionId { get; set; }
}

/// <summary>
/// Metadata model tracking a gambler's wager information related to win/lose games specifically
/// </summary>
public class KasinoWagerPredictionEventMetaModel : KasinoWagerBaseEventMetaModel
{
    /// <summary>
    /// The absolute time when the user predicted the thing was going to happen
    /// </summary>
    public required DateTimeOffset PredictedTime { get; set; }
}