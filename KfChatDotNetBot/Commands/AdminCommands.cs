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
        new Regex(@"^admin kick add (?<forum_id>\d+) (?<channel_id>\d+) (?<slug>\S+)$")
    ];

    public string? HelpText => "Add a Kick channel to the bot's database";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var channels = (await Helpers.GetValue(BuiltIn.Keys.KickChannels)).JsonDeserialize<List<KickChannelModel>>();
        var channelId = Convert.ToInt32(arguments["channel_id"].Value);
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
            ChannelSlug = arguments["slug"].Value
        });
        
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.KickChannels, channels);
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
        var channels = (await Helpers.GetValue(BuiltIn.Keys.KickChannels)).JsonDeserialize<List<KickChannelModel>>();
        var channelId = Convert.ToInt32(arguments["channel_id"].Value);
        var channel = channels.FirstOrDefault(ch => ch.ChannelId == channelId);
        if (channel == null)
        {
            await botInstance.SendChatMessageAsync("Channel is not in the database", true);
            return;
        }
        channels.Remove(channel);
        
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.KickChannels, channels);
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
        botInstance.BotServices.KickClient.Disconnect();
        await botInstance.SendChatMessageAsync("Disconnected from Kick. Client should reconnect shortly.", true);
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
        var hearings = (await Helpers.GetValue(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
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
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotCourtCalendar, hearings);
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
        var hearings = (await Helpers.GetValue(BuiltIn.Keys.BotCourtCalendar)).JsonDeserialize<List<CourtHearingModel>>();
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
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotCourtCalendar, hearings);
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
    public UserRight RequiredRight => UserRight.Admin;
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