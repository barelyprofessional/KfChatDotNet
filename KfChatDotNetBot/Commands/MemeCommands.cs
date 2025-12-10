using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;
using Zalgo;

namespace KfChatDotNetBot.Commands;

public class InsanityCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^insanity")];
    public string? HelpText => "Insanity";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("definition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEFdefinition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEF");
    }
}

public class TwistedCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^twisted")];
    public string? HelpText => "Get it twisted";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
    }
}

public class ScratchCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^scratch")];
    public string? HelpText => "Start scratching";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("🐀 🗣 GET IT TWISTED 🌪, SCRATCH ✅. PLEASE START SCRATCHING 👍. SCRATCHING YOUR SCABIES SORES IS RELIEF 😌 AND RELIEF ONLY 👍. YOU WILL FEEL BETTER 💪, YOU WILL FIND COMFORT ❗️. YOU WILL DO ALL OF THAT 💯, YOU UNDERSTAND ⁉️ YOU WILL CONQUER THE ITCH 🦠 AND REBUILD YOUR SKIN’S PEACE 🤯", true);
    }
}

public class CrackedCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^cracked (?<msg>.+)"),
        new Regex("^crackhead (?<msg>.+)")
    ];
    public string? HelpText => "Crackhead Zalgo text";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var msg = arguments["msg"].Value.TrimStart('/');
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.CrackedZalgoFuckUpMode, BuiltIn.Keys.CrackedZalgoFuckUpPosition
        ]);
        var zalgo = new ZalgoString(msg, (FuckUpMode)settings[BuiltIn.Keys.CrackedZalgoFuckUpMode].ToType<int>(),
            (FuckUpPosition)settings[BuiltIn.Keys.CrackedZalgoFuckUpPosition].ToType<int>());
        logger.Info($"Zalgo length: {zalgo.ToString().Length}");
        await botInstance.SendChatMessageAsync(zalgo.ToString(), true, ChatBot.LengthLimitBehavior.TruncateExactly);
    }
}

public class CleanCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^clean")
    ];
    public string? HelpText => "How long has Bossman been clean?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.BotCleanStartTime, BuiltIn.Keys.TwitchBossmanJackUsername]);
        var start = settings[BuiltIn.Keys.BotCleanStartTime];
        if (start.Value == null)
        {
            await botInstance.SendChatMessageAsync("Austin's sobriety start date was null", true);
            return;
        }
        var timespan = DateTimeOffset.UtcNow - DateTimeOffset.Parse(start.Value);
        await botInstance.SendChatMessageAsync($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} has been clean {timespan.Humanize(precision: 5)}", true);
    }
}

public class RehabCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^rehab")
    ];
    public string? HelpText => "How long until rehab is over?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.BotRehabEndTime, BuiltIn.Keys.TwitchBossmanJackUsername]);
        var end = settings[BuiltIn.Keys.BotRehabEndTime];
        if (end.Value == null)
        {
            await botInstance.SendChatMessageAsync("Austin's rehab end date was null", true);
            return;
        }

        var endDate = DateTimeOffset.Parse(end.Value);
        var timespan = endDate - DateTimeOffset.UtcNow;
        if (endDate > DateTimeOffset.UtcNow)
        {
            await botInstance.SendChatMessageAsync($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} should finish rehab in {timespan.Humanize(precision: 3)}", true);
            return;
        }
        await botInstance.SendChatMessageAsync($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} left rehab {timespan.Humanize(precision: 3)} ago", true);
    }
}

public class NextPoVisitCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^po "),
        new Regex("^po$")
    ];
    public string? HelpText => "How long until the next PO visit?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(120);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var time = await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotPoNextVisit);
        if (time.Value == null)
        {
            await botInstance.SendChatMessageAsync("There is no next PO visit :(", true);
            return;
        }
        var timespan = DateTimeOffset.Parse(time.Value) - DateTimeOffset.UtcNow;
        if (timespan.TotalSeconds < 0)
        {
            await botInstance.SendChatMessageAsync("It's over", true);
            return;
        }
        var sent = await botInstance.SendChatMessageAsync($"Austin's next PO visit will be in roughly {timespan.Humanize(precision: 10, minUnit: TimeUnit.Millisecond)}", true);
        while (sent.Status != SentMessageTrackerStatus.ResponseReceived)
        {
            await Task.Delay(250, ctx);
        }
        var i = 0;
        while (i < 60)
        {
            await Task.Delay(1000, ctx);
            timespan = DateTimeOffset.Parse(time.Value) - DateTimeOffset.UtcNow;
            await botInstance.KfClient.EditMessageAsync(sent.ChatMessageId!.Value,
                $"Austin's next PO visit will be in roughly {timespan.Humanize(precision: 10, minUnit: TimeUnit.Millisecond)}");
            i++;
        }
    }
}

public class NextCourtHearingCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^court"),
    ];
    public string? HelpText => "How long until the next court hearing?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(120);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var hearings = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
        if (hearings == null)
        {
            await botInstance.SendChatMessageAsync("Caught a null when grabbing hearings", true);
            return;
        }

        if (RenderHearings(hearings) == string.Empty)
        {
            await botInstance.SendChatMessageAsync("There are no upcoming hearings in the bot's calendar", true);
            return;
        }

        var sent = await botInstance.SendChatMessageAsync(RenderHearings(hearings), true);
        while (sent.Status != SentMessageTrackerStatus.ResponseReceived)
        {
            await Task.Delay(250, ctx);
        }
        var i = 0;
        while (i < 60)
        {
            await Task.Delay(1000, ctx);
            await botInstance.KfClient.EditMessageAsync(sent.ChatMessageId!.Value, RenderHearings(hearings));
            i++;
        }
    }

    private static string RenderHearings(List<CourtHearingModel> hearings)
    {
        var i = 0;
        var result = string.Empty;
        foreach (var hearing in hearings)
        {
            i++;
            var timespan = hearing.Time - DateTimeOffset.UtcNow;
            if (timespan.TotalSeconds < 0) continue; // Already passed
            result +=
                //$"{i}: [url=https://eapps.courts.state.va.us/ocis/details;fromOcis=true;fullcaseNumber=109{hearing.CaseNumber.Replace("-", string.Empty)}]{hearing.CaseNumber}[/url] " +
                $"{i}: {hearing.CaseNumber} " +
                $"{hearing.Description} will be heard in {timespan.Humanize(precision: 10, minUnit: TimeUnit.Second, maxUnit: TimeUnit.Year)}\r\n";
        }
        return result.Trim();
    }
}

public class JailCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^jail")
    ];
    public string? HelpText => "How long has Bossman been in jail?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([BuiltIn.Keys.BotJailStartTime, BuiltIn.Keys.TwitchBossmanJackUsername]);
        var start = settings[BuiltIn.Keys.BotJailStartTime];
        if (start.Value == null)
        {
            await botInstance.SendChatMessageAsync("He ain't in jail nigga", true);
            return;
        }
        var timespan = DateTimeOffset.UtcNow - DateTimeOffset.Parse(start.Value);
        await botInstance.SendChatMessageAsync($"{settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value} has been in jail {timespan.Humanize(precision: 5)}", true);
    }
}

public class LastStreamCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^laststream")];
    public string? HelpText => "How long ago did Austin Gambles last stream (on Twitch)?";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive, BuiltIn.Keys.TwitchBossmanJackUsername
        ]);
        var username = settings[BuiltIn.Keys.TwitchBossmanJackUsername].Value;
        var isLive = settings[BuiltIn.Keys.TwitchGraphQlPersistedCurrentlyLive].ToBoolean();
        if (isLive)
        {
            await botInstance.SendChatMessageAsync($"{username} is currently live on Twitch https://twitch.tv/{username}", true);
            return;
        }
        await using var db = new ApplicationDbContext();
        var latest = db.TwitchViewCounts.OrderByDescending(x => x.Id).FirstOrDefault();
        if (latest == null)
        {
            await botInstance.SendChatMessageAsync("The Twitch view counts table is empty", true);
            return;
        }

        var timespan = DateTimeOffset.UtcNow - latest.Time;
        var agt = TimeZoneInfo.ConvertTime(latest.Time, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
        // The table doesn't contain the name of the person so we'll just have to assume it's his Twitch username
        await botInstance.SendChatMessageAsync($"{username} last streamed on Twitch approximately {timespan.Humanize(precision: 2, minUnit: TimeUnit.Minute, maxUnit: TimeUnit.Hour)} ago at {agt:dddd h:mm tt} AGT", true);
    }
}

public class AlmanacCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^almanac", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Return details on how to submit almanac entries";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var text = await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotAlmanacText);
        if (message.MessageRaw.Contains("almanac plain"))
        {
            await botInstance.SendChatMessageAsync($"@{user.KfUsername}, [plain]{text.Value}", true);
            return;
        }
        await botInstance.SendChatMessageAsync($"@{user.KfUsername}, {text.Value}", true);
    }
}

public class JuiceSportsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^juicesports", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Juicesports LFG!";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await botInstance.SendChatMessageAsync(":juice: [img]https://i.ddos.lgbt/u/3GJtHq.gif[/img] :juice: [br]⠀⠀⠀⠀⠀⠀⠀" +
                                               "[img]https://i.ddos.lgbt/u/KAwWMW.webp[/img][br]⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀" +
                                               "[img]https://i.ddos.lgbt/u/uCuSOw.gif[/img][br]", true);
    }
}


public class HostessCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^hostess", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "Ask the hostess for help";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(30)
    };

    private static string[] StaticResponses = [
        "For questions regarding your current contract please contact us at contact@bossmanjack.com",
        "Unspecified error",
        "Have you considered giving us a review on TrustPilot?",
        "We are sincerely sorry to hear that you are not having a positive experience on our platform. Please be assured that we take matters of fairness and transparency very seriously.",
        "At the Kasino, we prioritize strict adherence to regulatory requirements to maintain the security and integrity of our platform.",
        "There are currently no hosts online to serve your request.",
        "We would like to assist you further and understand better the issue. Due to that, we have requested further information.",
        "When it comes to RTP, it is important to understand that this number is calculated based on at least 1 million bets. So, over a session of a few thousand bets, anything can happen, which is exactly what makes gambling exciting.",
        "We understand that gambling involves risks, and while some players may experience periods of winning and losing, we strive to provide resources and tools to support responsible gambling practices.",
        "Thank you for taking the time to leave a 5-star review! We're thrilled to have provided you with a great experience.",
        "Please rest assured that our platform operates with certified random number generators to ensure fairness and transparency in all gaming outcomes. We do not manipulate the odds or monitor games to favor any particular outcome.",
        "We would like to inform you that we have responded to your recent post.",
        "All of our Kasino originals are 100% probably fair and each and every single bet placed at our any games are verifiable.",
        "We want to emphasize that our games are developed with the highest standards of integrity and fairness.",
        "Stop harrassing me",
    ];

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var activeExclusion = await Money.GetActiveExclusionAsync(user.Id, ctx);
        if (db.Exclusions.Any(e => e.Id == user.Id))
        {
            await botInstance.SendChatMessageAsync("You are currently excluded from using hostess service.", true);
            return;
        }

        var random = new Random();
        var response = StaticResponses[random.Next(0, StaticResponses.Length)];
        await botInstance.SendChatMessageAsync(response, true);
    }
}