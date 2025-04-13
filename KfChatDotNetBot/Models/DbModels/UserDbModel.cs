using System.ComponentModel;

namespace KfChatDotNetBot.Models.DbModels;

public class UserDbModel
{
    public int Id { get; set; }
    public required string KfUsername { get; set; }
    public int KfId { get; set; }
    public UserRight UserRight { get; set; } = UserRight.Guest;
    public bool Ignored { get; set; } = false;
}

public enum UserRight
{
    Admin = 1000,
    [Description("True and Honest")]
    TrueAndHonest = 100,
    [Description("Rat")]
    Guest = 10,
    Loser = 0
}

public enum WhoWasActivityType
{
    Join,
    Part,
    Message
}

public class UserWhoWasDbModel
{
    public int Id { get; set; }
    public required UserDbModel User { get; set; }
    public required DateTimeOffset FirstOccurence { get; set; }
    public required DateTimeOffset LatestOccurence { get; set; }
    public required WhoWasActivityType ActivityType { get; set; }
}