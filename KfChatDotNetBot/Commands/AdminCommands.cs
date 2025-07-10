using System.Runtime.Caching;
using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class SetRoleCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin role set (?<user>\d+) (?<role>\d+)$"),
        new Regex(@"^admin right set (?<user>\d+) (?<role>\d+)$")
    ];

    public string? HelpText => "Set a user's role";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var targetUserId = Convert.ToInt32(arguments["user"].Value);
        var role = (UserRight)Convert.ToInt32(arguments["role"].Value);
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == targetUserId, cancellationToken: ctx);
        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync($"User '{targetUserId}' does not exist", true);
            return;
        }

        targetUser.UserRight = role;
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, {targetUser.KfUsername}'s role set to {role.Humanize()}", true);
    }
}

public class ToggleLiveStatusAdminCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin toggle livestatus$")
    ];

    public string? HelpText => "Toggle Bossman's live status so off screen gamba can be relayed";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.BotServices.IsBmjLive = !botInstance.BotServices.IsBmjLive;

        await botInstance.SendChatMessageAsync($"IsBmjLive => {botInstance.BotServices.IsBmjLive}", true);
    }
}

public class CacheClearAdminCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin cache clear$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cacheKeys = MemoryCache.Default.Select(kvp => kvp.Key).ToList();
        foreach (var cacheKey in cacheKeys)
        {
            MemoryCache.Default.Remove(cacheKey);
        }
        await botInstance.SendChatMessageAsync("Cache wiped", true);
    }
}

public class NewKickChannelCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin kick add (?<forum_id>\d+) (?<channel_id>\d+) (?<slug>\S+)$"),
        new Regex(@"^admin kick add (?<forum_id>\d+) (?<channel_id>\d+) (?<slug>\S+) (?<auto_capture>true|false)$")
    ];

    public string? HelpText => "Add a Kick channel to the bot's database";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var autoCapture = false;
        if (arguments.TryGetValue("auto_capture", out var argument))
        {
            autoCapture = argument.Value == "true";
        }
        var channels = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KickChannels)).JsonDeserialize<List<KickChannelModel>>();
        var channelId = Convert.ToInt32(arguments["channel_id"].Value);
        channels ??= [];
        if (channels.Any(channel => channel.ChannelId == channelId))
        {
            await botInstance.SendChatMessageAsync("Channel is already in the database", true);
            return;
        }

        var forumId = Convert.ToInt32(arguments["forum_id"].Value);
        channels.Add(new KickChannelModel
        {
            ChannelId = channelId,
            ForumId = forumId,
            ChannelSlug = arguments["slug"].Value,
            AutoCapture = autoCapture
        });
        
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KickChannels, channels);
        await botInstance.SendChatMessageAsync("Updated list of channels", true);
    }
}

public class RemoveKickChannelCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin kick remove (?<channel_id>\d+)$")
    ];

    public string? HelpText => "Remove a Kick channel from the bot's database";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var channels = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KickChannels)).JsonDeserialize<List<KickChannelModel>>();
        if (channels == null) throw new Exception("Caught a null when deserializing Kick channels");
        var channelId = Convert.ToInt32(arguments["channel_id"].Value);
        var channel = channels.FirstOrDefault(ch => ch.ChannelId == channelId);
        if (channel == null)
        {
            await botInstance.SendChatMessageAsync("Channel is not in the database", true);
            return;
        }
        channels.Remove(channel);
        
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.KickChannels, channels);
        await botInstance.SendChatMessageAsync("Updated list of channels", true);
    }
}

public class ReconnectKickCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin kick reconnect$")
    ];

    public string? HelpText => "Disconnect from Kick so the watchdog can reconnect it";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        if (botInstance.BotServices.KickClient == null)
        {
            await botInstance.SendChatMessageAsync("Kick client is not initialized", true);
            return;
        }
        botInstance.BotServices.KickClient.Disconnect();
        await botInstance.SendChatMessageAsync("Disconnected from Kick. Client should reconnect shortly.", true);
    }
}

public class NewPartiChannelCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin parti add (?<forum_id>\d+) (?<social>\S+) (?<username>\S+) (?<auto_capture>true|false)$"),
        new Regex(@"^admin parti add (?<forum_id>\d+) (?<social>\S+) (?<username>\S+)$")

    ];

    public string? HelpText => "Add a Parti channel to the bot's database";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var autoCapture = false;
        if (arguments.TryGetValue("auto_capture", out var argument))
        {
            autoCapture = argument.Value == "true";
        }
        var channels = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.PartiChannels)).JsonDeserialize<List<PartiChannelModel>>();
        var username = arguments["username"].Value;
        channels ??= [];
        if (channels.Any(channel => channel.Username == username))
        {
            await botInstance.SendChatMessageAsync("Channel is already in the database", true);
            return;
        }

        var forumId = Convert.ToInt32(arguments["forum_id"].Value);
        channels.Add(new PartiChannelModel
        {
            Username = username,
            ForumId = forumId,
            AutoCapture = autoCapture,
            SocialMedia = arguments["social"].Value
        });
        
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.PartiChannels, channels);
        await botInstance.SendChatMessageAsync("Updated list of channels", true);
    }
}

public class RemovePartiChannelCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin parti remove (?<username>\S+)$")
    ];

    public string? HelpText => "Remove a Parti channel from the bot's database";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var channels = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.PartiChannels)).JsonDeserialize<List<PartiChannelModel>>();
        if (channels == null) throw new Exception("Caught a null when deserializing Parti channels");
        var username = arguments["username"].Value;
        var channel = channels.FirstOrDefault(ch => ch.Username == username);
        if (channel == null)
        {
            await botInstance.SendChatMessageAsync("Channel is not in the database", true);
            return;
        }
        channels.Remove(channel);
        
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.PartiChannels, channels);
        await botInstance.SendChatMessageAsync("Updated list of channels", true);
    }
}

public class AddCourtHearingCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin hearing add (?<case>\S+) (?<date>\S+) (?<description>.+)$")
    ];

    public string? HelpText => "Add a court hearing to the bot's calendar";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var hearings = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
        if (hearings == null)
        {
            await botInstance.SendChatMessageAsync("Hearings list was null", true);
            return;
        }
        var caseNumber = arguments["case"].Value;
        if (!DateTimeOffset.TryParse(arguments["date"].Value, out var date))
        {
            await botInstance.SendChatMessageAsync("Failed to parse date", true);
            return;
        }

        hearings.Add(new CourtHearingModel {CaseNumber = caseNumber, Description = arguments["description"].Value, Time = date});
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.BotCourtCalendar, hearings);
        await botInstance.SendChatMessageAsync("Updated list of hearings", true);
    }
}

public class RemoveCourtHearingCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin hearing remove (?<index>\d+)$")
    ];

    public string? HelpText => "Remove a hearing from the bot's calendar";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var hearings = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
        if (hearings == null)
        {
            await botInstance.SendChatMessageAsync("Hearings list was null", true);
            return;
        }
        var hearingIndex = Convert.ToInt32(arguments["index"].Value);
        if (hearings.Count < hearingIndex)
        {
            await botInstance.SendChatMessageAsync(
                $"Index supplied is out of range. There are only {hearings.Count} hearings in the database", true);
            return;
        }
        
        hearings.RemoveAt(hearingIndex - 1);
        await SettingsProvider.SetValueAsJsonObjectAsync(BuiltIn.Keys.BotCourtCalendar, hearings);
        await botInstance.SendChatMessageAsync("Updated list of hearings", true);
    }
}

public class NonceLiveCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin togglenonce$")
    ];

    public string? HelpText => "Toggle IsChrisDjLive";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.BotServices.IsChrisDjLive = !botInstance.BotServices.IsChrisDjLive;
        await botInstance.SendChatMessageAsync($"IsChrisDjLive => {botInstance.BotServices.IsChrisDjLive}", true);
    }
}

public class DeleteMessagesCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin delete (?<msg_count>\d+)$")
    ];

    public string? HelpText => "Delete the most recent x number of messages";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var amount = int.Parse(arguments["msg_count"].Value);
        if (amount > 10)
        {
            await botInstance.SendChatMessageAsync("More than 10 messages seems like a bit much?", true);
            return;
        }
        var messages = botInstance.SentMessages.Where(msg => msg.Status == SentMessageTrackerStatus.ResponseReceived)
            .TakeLast(amount);
        foreach (var msg in messages)
        {
            if (msg.ChatMessageId == null)
            {
                continue;
            }

            await botInstance.KfClient.DeleteMessageAsync(msg.ChatMessageId.Value);
        }
    }
}

public class IgnoreCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin ignore (?<user_id>\d+)$")
    ];

    public string? HelpText => "Ignore a user by ID";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == int.Parse(arguments["user_id"].Value),
            cancellationToken: ctx);
        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync("Can't find user", true);
            return;
        }

        if (targetUser.Ignored)
        {
            await botInstance.SendChatMessageAsync($"User {targetUser.KfUsername} has already been ignored", true);
            return;
        }

        targetUser.Ignored = true;
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"Now ignoring {targetUser.KfUsername}", true);
    }
}

public class UnignoreCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin unignore (?<user_id>\d+)$")
    ];

    public string? HelpText => "Unignore a user by ID";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == int.Parse(arguments["user_id"].Value),
            cancellationToken: ctx);
        if (targetUser == null)
        {
            await botInstance.SendChatMessageAsync("Can't find user", true);
            return;
        }

        if (!targetUser.Ignored)
        {
            await botInstance.SendChatMessageAsync($"User {targetUser.KfUsername} is not ignored", true);
            return;
        }

        targetUser.Ignored = false;
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"No longer ignoring {targetUser.KfUsername}", true);
    }
}

public class SetAlmanacTextCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin almanac set text (?<text>.+)$")
    ];

    public string? HelpText => "Set the almanac text to whatever";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await SettingsProvider.SetValueAsync(BuiltIn.Keys.BotAlmanacText, arguments["text"].Value);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, updated text for the almanac shill", true);
    }
}

public class SetAlmanacIntervalCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin almanac set interval (?<interval>.+)$")
    ];

    public string? HelpText => "Set the almanac interval to whatever in seconds";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var interval = Convert.ToInt32(arguments["interval"].Value);
        if (interval < 300)
        {
            await botInstance.SendChatMessageAsync("Not going to let you use an interval below 300 seconds", true);
            return;
        }
        await SettingsProvider.SetValueAsync(BuiltIn.Keys.BotAlmanacInterval, arguments["interval"].Value);
        if (botInstance.BotServices.AlmanacShill == null)
        {
            await botInstance.SendChatMessageAsync("Value has been saved but almanac shill has not been initialized", true);
            return;
        }
        await botInstance.BotServices.AlmanacShill.StopShillTaskAsync();
        botInstance.BotServices.AlmanacShill.StartShillTask();
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, updated interval and restarted the shill task", true);
    }
}
public class StopAlmanacCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin almanac stop$")
    ];

    public string? HelpText => "Stop the almanac reminder";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (botInstance.BotServices.AlmanacShill == null)
        {
            await botInstance.SendChatMessageAsync("AlmanacShill is null", true);
            return;
        }
        if (!botInstance.BotServices.AlmanacShill.IsShillTaskRunning())
        {
            await botInstance.SendChatMessageAsync("Looks like the task isn't even running", true);
            return;
        }

        await botInstance.BotServices.AlmanacShill.StopShillTaskAsync();
        await botInstance.SendChatMessageAsync("Asked it nicely to stop", true);
    }
}

public class StartAlmanacCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^admin almanac start")
    ];

    public string? HelpText => "Start the almanac reminder";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (botInstance.BotServices.AlmanacShill == null)
        {
            await botInstance.SendChatMessageAsync("AlmanacShill is null", true);
            return;
        }
        if (botInstance.BotServices.AlmanacShill.IsShillTaskRunning())
        {
            await botInstance.SendChatMessageAsync("Looks like the task is already running", true);
            return;
        }

        botInstance.BotServices.AlmanacShill.StartShillTask();
        await botInstance.SendChatMessageAsync("Asked it nicely to start", true);
    }
}