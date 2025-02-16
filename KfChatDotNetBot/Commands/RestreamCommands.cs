using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

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
        var url = await Helpers.GetValue(BuiltIn.Keys.RestreamUrl);
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
        await Helpers.SetValue(BuiltIn.Keys.RestreamUrl, arguments["url"].Value);
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
        var channels = Helpers.GetValue(BuiltIn.Keys.KickChannels).Result.JsonDeserialize<List<KickChannelModel>>();
        var channel = channels.FirstOrDefault(ch => ch.ForumId == user.KfId);
        if (channel == null)
        {
            await botInstance.SendChatMessageAsync("You have no stream.", true);
            return;
        }

        await botInstance.SendChatMessageAsync($"@{user.KfUsername} is a weirdo who streams. Come check out his channel at https://kick.com/{channel.ChannelSlug}", true);
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
        var url = await Helpers.GetValue(BuiltIn.Keys.RestreamUrl);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, restream URL: [plain]{url.Value}", true);
    }
}