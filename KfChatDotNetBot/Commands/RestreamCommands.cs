using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace KfChatDotNetBot.Commands;

public class GetRestreamCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^restream$")
    ];

    public string? HelpText => "Grab restream URL";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var url = await SettingsProvider.GetValueAsync(BuiltIn.Keys.RestreamUrl);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, restream URL: {url.Value}", true);
    }
}

public class SetRestreamCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^restream set (?<url>.+)$")
    ];

    public string? HelpText => "Set restream URL";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await SettingsProvider.SetValueAsync(BuiltIn.Keys.RestreamUrl, arguments["url"].Value);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, updated URL", true);
    }
}

public class SelfPromoCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^selfpromo")
    ];

    public string? HelpText => "Promote your shit";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await using var db = new ApplicationDbContext();
        db.Users.Attach(user);
        var streams = await db.Streams.Where(s => s.User == user).ToListAsync(ctx);
        if (streams.Count == 0)
        {
            await botInstance.SendChatMessageAsync("You have no streams", true);
            return;
        }
        
        var streamList = streams.Aggregate(string.Empty, (current, stream) => current + $"[br]- {stream.StreamUrl}");

        await botInstance.SendChatMessageAsync(
            $"@{user.KfUsername} is a weirdo who streams a lot. His channels are at: {streamList}", true);
    }
}

public class GetRestreamPlainCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^restream plain$")
    ];

    public string? HelpText => "Grab restream URL with plain prefixed";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var url = await SettingsProvider.GetValueAsync(BuiltIn.Keys.RestreamUrl);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, restream URL: [plain]{url.Value}", true);
    }
}