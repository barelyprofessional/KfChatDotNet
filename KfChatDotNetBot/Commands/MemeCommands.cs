using System.Text.RegularExpressions;
using Humanizer;
using Humanizer.Localisation;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
    }
}

public class HelpMeCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^helpme")];
    public string? HelpText => "Somebody please help me";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("[img]https://i.postimg.cc/fTw6tGWZ/ineedmoneydumbfuck.png[/img]", true);
    }
}

public class SentCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^sent$")];
    public string? HelpText => "Sent love";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        await botInstance.SendChatMessageAsync("[img]https://i.ibb.co/GHq7hb1/4373-g-N5-HEH2-Hkc.png[/img]", true);
    }
}

public class GmKasinoCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^gmkasino")];
    public string? HelpText => "Good Morning Kasino";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGmKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null) return;
        var random = new Random();
        var image = images[random.Next(images.Count)];
        await botInstance.SendChatMessageAsync($"[img]{image}[/img]", true);
    }
}

public class GnKasinoCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^gnkasino")];
    public string? HelpText => "Good Night, Kasino";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGnKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null) return;
        var random = new Random();
        var image = images[random.Next(images.Count)];
        await botInstance.SendChatMessageAsync($"[img]{image}[/img]", true);
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var msg = arguments["msg"].Value.TrimStart('/');
        var settings = await Helpers.GetMultipleValues([
            BuiltIn.Keys.CrackedZalgoFuckUpMode, BuiltIn.Keys.CrackedZalgoFuckUpPosition
        ]);
        var zalgo = new ZalgoString(msg, (FuckUpMode)settings[BuiltIn.Keys.CrackedZalgoFuckUpMode].ToType<int>(),
            (FuckUpPosition)settings[BuiltIn.Keys.CrackedZalgoFuckUpPosition].ToType<int>());
        logger.Info($"Zalgo length: {zalgo.ToString().Length}");
        await botInstance.SendChatMessageAsync(zalgo.ToString(), true, ChatBot.LengthLimitBehavior.TruncateExactly);
    }
}

public class WinmanjackCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^winmanjack")
    ];
    public string? HelpText => "winmanjack.jpg";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var image = await Helpers.GetValue(BuiltIn.Keys.WinmanjackImgUrl);
        await botInstance.SendChatMessageAsync($"[img]{image.Value}[/img]", true);
    }
}

public class PraygeCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^prayge")
    ];
    public string? HelpText => "prayge.jpg";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var image = await Helpers.GetValue(BuiltIn.Keys.BotPraygeImgUrl);
        await botInstance.SendChatMessageAsync($"[img]{image.Value}[/img]", true);
    }
}

public class CrackpipeCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^crackpipe")
    ];
    public string? HelpText => "crackpipe.gif";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var image = await Helpers.GetValue(BuiltIn.Keys.BotCrackpipeImgUrl);
        await botInstance.SendChatMessageAsync($"[img]{image.Value}[/img]", true);
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var start = await Helpers.GetValue(BuiltIn.Keys.BotCleanStartTime);
        if (start.Value == null)
        {
            await botInstance.SendChatMessageAsync("Bossman's sobriety start date was null", true);
            return;
        }
        var timespan = DateTimeOffset.UtcNow - DateTimeOffset.Parse(start.Value);
        await botInstance.SendChatMessageAsync($"Bossman has been clean {timespan.Humanize(precision:5)}", true);
    }
}

public class RehbCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^rehab")
    ];
    public string? HelpText => "How long until rehab is over?";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var end = await Helpers.GetValue(BuiltIn.Keys.BotRehabEndTime);
        if (end.Value == null)
        {
            await botInstance.SendChatMessageAsync("Bossman's rehab end date was null", true);
            return;
        }
        var timespan = DateTimeOffset.Parse(end.Value) - DateTimeOffset.UtcNow;
        await botInstance.SendChatMessageAsync($"Bossman was kicked out of rehab {timespan.Humanize(precision:3)} ago", true);
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var time = await Helpers.GetValue(BuiltIn.Keys.BotPoNextVisit);
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
        var sent = await botInstance.SendChatMessageAsync($"Bossman's next PO visit will be in roughly {timespan.Humanize(precision: 10, minUnit: TimeUnit.Millisecond)}", true);
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
                $"Bossman's next PO visit will be in roughly {timespan.Humanize(precision: 10, minUnit: TimeUnit.Millisecond)}");
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var hearings = (await Helpers.GetValue(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
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

        var sent = await botInstance.SendChatMessageAsync(RenderHearings(hearings),true);
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
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var start = await Helpers.GetValue(BuiltIn.Keys.BotJailStartTime);
        if (start.Value == null)
        {
            await botInstance.SendChatMessageAsync("He ain't in jail nigga", true);
            return;
        }
        var timespan = DateTimeOffset.UtcNow - DateTimeOffset.Parse(start.Value);
        await botInstance.SendChatMessageAsync($"Bossman has been in jail {timespan.Humanize(precision:5)}", true);
    }
}