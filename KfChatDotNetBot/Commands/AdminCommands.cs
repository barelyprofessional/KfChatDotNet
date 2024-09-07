﻿using System.Text.RegularExpressions;
using Humanizer;
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
            botInstance.SendChatMessage($"User '{targetUserId}' does not exist", true);
            return;
        }

        targetUser.UserRight = role;
        await db.SaveChangesAsync(ctx);
        botInstance.SendChatMessage($"@{message.Author.Username}, {targetUser.KfUsername}'s role set to {role.Humanize()}", true);
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
            botInstance.SendChatMessage("Images list was null", true);
            return;
        }
        var newImage = arguments["image"].Value;
        if (images.Contains(newImage))
        {
            botInstance.SendChatMessage("Image is already in the list", true);
            return;
        }

        images.Add(newImage);
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotGmKasinoImageRotation, images);
        botInstance.SendChatMessage("Updated list of images", true);
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
            botInstance.SendChatMessage("Images list was null", true);
            return;
        }
        var targetImage = arguments["image"].Value;
        if (!images.Contains(targetImage))
        {
            botInstance.SendChatMessage("Image is not in the list", true);
            return;
        }

        images.Remove(targetImage);
        await Helpers.SetValueAsJsonObject(BuiltIn.Keys.BotGmKasinoImageRotation, images);
        botInstance.SendChatMessage("Updated list of images", true);
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
            botInstance.SendChatMessage("Images list was null", true);
            return;
        }

        var result = "List of images:";
        var i = 0;
        foreach (var image in images)
        {
            i++;
            result += $"[br]{i}: {image}";
        }

        botInstance.SendChatMessage(result, true);
    }
}

public class ToggleLiveStatusCommand : ICommand
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

        botInstance.SendChatMessage($"IsBmjLive => {botInstance.BotServices.IsBmjLive}", true);
    }
}