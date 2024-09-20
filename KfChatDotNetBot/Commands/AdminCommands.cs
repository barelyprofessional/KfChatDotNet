﻿using System.Runtime.Caching;
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

public class GmKasinoAddCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin gmkasino add (?<image>.+)$")
    ];

    public string? HelpText => "Add an image to the gmkasino image list";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGmKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null)
        {
            await botInstance.SendChatMessageAsync("Images list was null", true);
            return;
        }
        var newImage = arguments["image"].Value;
        if (images.Contains(newImage))
        {
            await botInstance.SendChatMessageAsync("Image is already in the list", true);
            return;
        }

        images.Add(newImage);
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotGmKasinoImageRotation, images);
        await botInstance.SendChatMessageAsync("Updated list of images", true);
    }
}

public class GmKasinoRemoveCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin gmkasino remove (?<image>.+)$")
    ];

    public string? HelpText => "Remove an image in the gmkasino image list";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGmKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null)
        {
            await botInstance.SendChatMessageAsync("Images list was null", true);
            return;
        }
        var targetImage = arguments["image"].Value;
        if (!images.Contains(targetImage))
        {
            await botInstance.SendChatMessageAsync("Image is not in the list", true);
            return;
        }

        images.Remove(targetImage);
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotGmKasinoImageRotation, images);
        await botInstance.SendChatMessageAsync("Updated list of images", true);
    }
}

public class GmKasinoListCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin gmkasino list$")
    ];

    public string? HelpText => "Dump out the list of images for gmkasino";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGmKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null)
        {
            await botInstance.SendChatMessageAsync("Images list was null", true);
            return;
        }

        var result = "List of images:";
        var i = 0;
        foreach (var image in images)
        {
            i++;
            result += $"[br]{i}: {image}";
        }

        await botInstance.SendChatMessageAsync(result, true);
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