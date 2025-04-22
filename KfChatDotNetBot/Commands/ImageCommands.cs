using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class AddImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin image (?<key>\w+) add (?<url>.+)$"),
        new Regex(@"^admin images (?<key>\w+) add (?<url>.+)$")
    ];
    public string? HelpText => "Add an image to the image rotation specified";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await Helpers.GetValue(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value;
        var url = arguments["url"].Value;
        if (!imageKeys.Contains(key))
        {
            await botInstance.SendChatMessageAsync(
                $"Key you specified is not supported. Available keys are: {string.Join(' ', imageKeys)}", true);
            return;
        }

        if (await db.Images.AnyAsync(i => i.Key == key && i.Url == url, ctx))
        {
            await botInstance.SendChatMessageAsync("This image already exists in the database with this key", true);
            return;
        }

        await db.Images.AddAsync(new ImageDbModel { Key = key, Url = url, LastSeen = DateTimeOffset.MinValue }, ctx);
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync("Added image to database", true);
    }
}

public class RemoveImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin image (?<key>\w+) remove (?<url>.+)$"),
        new Regex(@"^admin images (?<key>\w+) remove (?<url>.+)$"),
        new Regex(@"^admin image (?<key>\w+) delete (?<url>.+)$"),
        new Regex(@"^admin images (?<key>\w+) delete (?<url>.+)$")
    ];
    public string? HelpText => "Remove an image from the image rotation specified";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await Helpers.GetValue(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value;
        var url = arguments["url"].Value;
        if (!imageKeys.Contains(key))
        {
            await botInstance.SendChatMessageAsync(
                $"Key you specified is not supported. Available keys are: {string.Join(' ', imageKeys)}", true);
            return;
        }

        var image = await db.Images.FirstOrDefaultAsync(i => i.Key == key && i.Url == url, ctx);
        if (image == null)
        {
            await botInstance.SendChatMessageAsync("This image isn't in the database with this key", true);
            return;
        }

        db.Images.Remove(image);
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync("Removed image from database", true);
    }
}

public class ListImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin image (?<key>\w+) list$"),
        new Regex(@"^admin images (?<key>\w+) list$")
    ];
    public string? HelpText => "Remove an image from the image rotation specified";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await Helpers.GetValue(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value;
        if (!imageKeys.Contains(key))
        {
            await botInstance.SendChatMessageAsync(
                $"Key you specified is not supported. Available keys are: {string.Join(' ', imageKeys)}", true);
            return;
        }

        var images = db.Images.Where(i => i.Key == key);
        var i = 0;
        var result = $"List of images for {key}:";
        foreach (var image in images)
        {
            i++;
            result += $"[br]{i}: {image.Url} (Last seen {image.LastSeen:yyyy-MM-dd HH:mm:ss zz})";
        }

        await botInstance.SendChatMessagesAsync(result.FancySplitMessage(partSeparator: "[br]"),
            bypassSeshDetect: true);
    }
}

[AllowAdditionalMatches]
public class GetRandomImage : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^(?<key>\w+)")
    ];
    public string? HelpText => "Get a random image";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var key = arguments["key"].Value.ToLower();
        var images = db.Images.Where(i => i.Key == key);
        if (!await images.AnyAsync(ctx)) return;
        var divideBy = (await Helpers.GetValue(BuiltIn.Keys.BotImageRandomSliceDivideBy)).ToType<int>();
        var limit = 1;
        var count = await images.CountAsync(ctx);
        if (count > divideBy)
        {
            limit = count / divideBy;
        }

        // EF with SQLite can't sort on dates as it's just TEXT
        var selection = (await images.ToListAsync(ctx)).OrderBy(i => i.LastSeen).Take(limit).ToList();
        // MaxValue is never returned by Next so you don't need to -1 for indexing
        var image = selection[new Random().Next(0, selection.Count)];
        image.LastSeen = DateTimeOffset.UtcNow;
        db.Images.Update(image);
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync($"[img]{image.Url}[/img]", true);
    }
}