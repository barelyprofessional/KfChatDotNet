using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Humanizer;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class AddImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (image|images) (?<key>\w+) (add|add_nigger) (?<url>\S+) (?<raw>raw) (?<tags>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^admin (image|images) (?<key>\w+) (add|add_nigger) (?<url>\S+) (?<tags>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^admin (image|images) (?<key>\w+) (add|add_nigger) (?<url>\S+)$", RegexOptions.IgnoreCase)
    ];
    public string HelpText => "Add an image to the image rotation specified";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value;
        var url = arguments["url"].Value;
        var tags = arguments.TryGetValue("tags", out var tagsArg) ? tagsArg.Value.ToLower().Split(" ").ToList() : [];
        var niggerMode = message.Message.Contains("add_nigger");
        // TODO: Implement real and raw mode
        //var _rawMode = arguments.ContainsKey("raw");
        if (!imageKeys.Contains(key))
        {
            await botInstance.SendChatMessageAsync(
                $"Key you specified is not supported. Available keys are: {imageKeys.Humanize()}", true);
            return;
        }

        if (!niggerMode && await db.Images.AnyAsync(i => i.Key == key && i.Url == url, ctx))
        {
            await botInstance.SendChatMessageAsync("This image already exists in the database with this key", true);
            return;
        }
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"The URL '{url}' you provided is not valid");
            return;
        }

        var result = url;
        // todo add automatic compression/re-upload and raw mode option

        await db.Images.AddAsync(new ImageDbModel
        {
            Key = key, Url = result, LastSeen = DateTimeOffset.MinValue, TagList = tags,
            Metadata = new ImageMetadataModel { AddedByUserId = user.Id, WhenAdded = DateTimeOffset.UtcNow }
        }, ctx);
        var count = await db.Images.Where(i => i.Key == key).CountAsync(cancellationToken: ctx);
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you added the following media to the {key} carousel which now has {count:N0} images[spoiler=\"Image\"][img]{url}[/img]", true);
    }
}

public class AddImageTagsCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (image|images) tag (?<id>\d+) (?<tags>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^(image|images) tag (?<id>\d+) (?<tags>.+)$", RegexOptions.IgnoreCase),
    ];
    public string HelpText => "Add tags to an image";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var id = Convert.ToInt32(arguments["id"].Value);
        var tags = arguments["tags"].Value.ToLower()
            .Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var image = await db.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken: ctx);
        if (image == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, the image ID you specified does not exist", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        if (tags.Any(tag => tag.Length > 50))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, tag length limit is 50 characters",
                true);
            return;
        }

        image.TagList = image.TagList.Concat(tags).Distinct().ToList();
        if (image.TagList.Count > 50)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, {id} has a shitload of tags already!",
                true);
            return;
        }
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, updated tags for image ID {id} with {image.TagList.Humanize()}", true);
    }
}

public class UntagImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (image|images) untag (?<id>\d+)$", RegexOptions.IgnoreCase)
    ];
    public string HelpText => "Remove tags from an image";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var id = Convert.ToInt32(arguments["id"].Value);
        var image = await db.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken: ctx);
        if (image == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, the image ID you specified does not exist", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        image.TagList = [];
        await db.SaveChangesAsync(ctx);
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, removed tags from {id}", true);
    }
}

public class RemoveImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (image|images) (?<key>\w+) (remove|delete) (?<url>.+)$"),
    ];
    public string HelpText => "Remove an image from the image rotation specified";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
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
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, you removed the following media from the {key} carousel[spoiler=\"Image\"][img]{url}[/img]", true);
    }
}

public class ListImageCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (image|images) (?<key>\w+) list$"),
        new Regex(@"^(image|images) (?<key>\w+) list$"),
    ];
    public string HelpText => "List images for a given carousel";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel RateLimitOptions => new()
    {
        Flags = RateLimitFlags.None,
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(15)
    };
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var imageKeys = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys))
            .JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value;
        if (!imageKeys.Contains(key))
        {
            await botInstance.SendChatMessageAsync(
                $"Key you specified is not supported. Available keys are: {imageKeys.Humanize()}", true);
            return;
        }

        var images = db.Images.Where(i => i.Key == key);
        if (await images.CountAsync(cancellationToken: ctx) > 10 && await Zipline.IsZiplineEnabled())
        {
            var content = string.Empty;
            foreach (var image in images)
            {
                var ts = DateTimeOffset.UtcNow - image.LastSeen;
                var time = $"{ts.TotalDays:N0}d{ts.Hours:N0}h{ts.Minutes:N0}m{ts.Seconds:N0}s";
                content += $"{image.Url} (ID: {image.Id}) - {time} - {image.TagList.Humanize()}" + Environment.NewLine;
            }

            var paste = await Zipline.Upload(content, new MediaTypeHeaderValue("text/plain"), "1d", ctx);
            await botInstance.SendChatMessageAsync($"List of images for {key}: {paste}", true);
            return;
        }

        var i = 0;
        var result = $"List of images for {key}:";
        foreach (var image in images)
        {
            i++;
            var ts = DateTimeOffset.UtcNow - image.LastSeen;
            result += $"[br]{i}: {image.Url} (ID: {image.Id}) (Last seen {ts.TotalDays:N0}d{ts.Hours:N0}h{ts.Minutes:N0}m{ts.Seconds:N0}s ago)";
        }

        await botInstance.SendChatMessagesAsync(result.FancySplitMessage(partSeparator: "[br]"),
            bypassSeshDetect: true);
    }
}

public class ManageImageKeyCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^admin (imagekey|imageskey) (?<operation>add|remove|delete) (?<key>\w+)$"),
    ];
    public string HelpText => "Add or remove an acceptable image key from the BotImageAcceptableKeys setting";
    public UserRight RequiredRight => UserRight.Admin;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => true;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var imageKeys = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys)).JsonDeserialize<List<string>>();
        if (imageKeys == null) throw new InvalidOperationException($"{BuiltIn.Keys.BotImageAcceptableKeys} was null");
        var key = arguments["key"].Value.ToLower();
        var operation = arguments["operation"].Value.ToLower();

        if (operation is "add")
        {
            if (imageKeys.Contains(key))
            {
                await botInstance.ReplyToUser(message, $"Key \"{key}\" is already in the acceptable keys list", true);
                return;
            }
            imageKeys.Add(key);
            await SettingsProvider.SetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys, JsonSerializer.Serialize(imageKeys));
            await botInstance.ReplyToUser(message,
                $"Added key \"{key}\" to acceptable image keys. Current keys: {imageKeys.Humanize()}", true);
            return;
        }

        if (operation is "remove" or "delete")
        {
            if (!imageKeys.Contains(key))
            {
                await botInstance.ReplyToUser(message,
                    $"Key \"{key}\" is not in the acceptable keys list. Current keys: {imageKeys.Humanize()}", true);
                return;
            }
            imageKeys.Remove(key);
            await SettingsProvider.SetValueAsync(BuiltIn.Keys.BotImageAcceptableKeys, JsonSerializer.Serialize(imageKeys));
            await botInstance.ReplyToUser(message,
                $"Removed key \"{key}\" from acceptable image keys. Current keys: {imageKeys.Humanize()}", true);
            return;
        }

        await botInstance.ReplyToUser(message, $"Operation '{operation}' not supported", true);
    }
}

[AllowAdditionalMatches]
public class GetRandomImage : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^(?<key>\w+)$"),
        new Regex(@"^(?<key>\w+) (?<search>.+)")
    ];
    public string HelpText => "Get a random image";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromMinutes(10);
    public RateLimitOptionsModel RateLimitOptions => new()
    {
        Window = TimeSpan.FromSeconds(30),
        MaxInvocations = 7,
        Flags = RateLimitFlags.UseEntireMessage | RateLimitFlags.NoResponse
    };
    public bool WhisperCanInvoke => false;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        var key = arguments["key"].Value.ToLower();
        var searchTerm = arguments.TryGetValue("search", out var searchArg) ? searchArg.Value.ToLower().Trim() : null;
        var images = db.Images.Where(i => i.Key == key);
        if (!await images.AnyAsync(ctx))
        {
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (key == "sloppa" && user.UserRight < UserRight.TrueAndHonest)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.FormatUsername()}, sloppa requires at least {UserRight.TrueAndHonest.Humanize()}");
            return;
        }
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotImageRandomSliceDivideBy, BuiltIn.Keys.BotImagePigCubeSelfDestruct,
            BuiltIn.Keys.BotImageInvertedCubeUrl, BuiltIn.Keys.BotImagePigCubeSelfDestructMin,
            BuiltIn.Keys.BotImagePigCubeSelfDestructMax, BuiltIn.Keys.BotImageInvertedPigCubeSelfDestructDelay,
            BuiltIn.Keys.BotImageChinkSelfDestruct, BuiltIn.Keys.BotImageChinkSelfDestructDelay
        ]);

        var selection = await images.ToListAsync(ctx);
        if (!string.IsNullOrEmpty(searchTerm))
        {
            var searchTokens = searchTerm.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            selection = searchTokens.Aggregate(selection, (current, token) => 
                current.Where(i => i.TagList.Count > 0 && i.TagList.Contains(token)).ToList());
            if (selection.Count == 0)
            {
                RateLimitService.RemoveMostRecentEntry(user, this);
                await botInstance.SendChatMessageAsync($"No image in {key} matched \"{searchTerm}\"", true);
                return;
            }
        }
        var divideBy = settings[BuiltIn.Keys.BotImageRandomSliceDivideBy].ToType<int>();
        var limit = 1;
        var count = await images.CountAsync(ctx);
        if (count > divideBy)
        {
            limit = count / divideBy;
        }

        // EF with SQLite can't sort on dates as it's just TEXT
        selection = selection.OrderBy(i => i.LastSeen).Take(limit).ToList();
        // MaxValue is never returned by Next so you don't need to -1 for indexing
        var image = selection[new Random().Next(0, selection.Count)];
        image.LastSeen = DateTimeOffset.UtcNow;
        db.Images.Update(image);
        await db.SaveChangesAsync(ctx);
        TimeSpan? timeToDeletion = null;
        if (key == "pigcube" && settings[BuiltIn.Keys.BotImagePigCubeSelfDestruct].ToBoolean())
        {
            timeToDeletion = TimeSpan.FromMilliseconds(image.Url == settings[BuiltIn.Keys.BotImageInvertedCubeUrl].Value
                ? settings[BuiltIn.Keys.BotImageInvertedPigCubeSelfDestructDelay].ToType<int>()
                : new Random().Next(settings[BuiltIn.Keys.BotImagePigCubeSelfDestructMin].ToType<int>(),
                    settings[BuiltIn.Keys.BotImagePigCubeSelfDestructMax].ToType<int>()));
        }
        else if (key is "chink" or "sloppa" && settings[BuiltIn.Keys.BotImageChinkSelfDestruct].ToBoolean())
        {
            RateLimitService.AddEntry(user, this, message.MessageRawHtmlDecoded);
            timeToDeletion = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.BotImageChinkSelfDestructDelay].ToType<int>());
        }

        var tagNag = string.Empty;
        if (image.TagList.Count == 0)
        {
            tagNag = $"[br]This image has no tags. You can add some using [ditto]!images tag {image.Id}[/ditto]";
        }
        await botInstance.SendChatMessageAsync($"[img]{image.Url}[/img]{tagNag}", true, autoDeleteAfter: timeToDeletion);
    }
}