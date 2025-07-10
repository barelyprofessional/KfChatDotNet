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

public class SetShillRestreamCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^restream setshill (?<url>.+)$")
    ];

    public string? HelpText => "Set restream shill URL";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        await SettingsProvider.SetValueAsync(BuiltIn.Keys.TwitchCommercialRestreamShillMessage, arguments["url"].Value);
        await botInstance.SendChatMessageAsync($"@{message.Author.Username}, updated URL for the commercial break restream shill", true);
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
        var channels = SettingsProvider.GetValueAsync(BuiltIn.Keys.KickChannels).Result.JsonDeserialize<List<KickChannelModel>>();
        var partiChannels = SettingsProvider.GetValueAsync(BuiltIn.Keys.PartiChannels).Result
            .JsonDeserialize<List<PartiChannelModel>>();
        if (channels == null || partiChannels == null)
        {
            await botInstance.SendChatMessageAsync("For some reason the list of Kick or Parti channels deserialized to null", true);
            return;
        }
        
        var userChannels = channels.Where(ch => ch.ForumId == user.KfId).ToList();
        var userPartiChannels = partiChannels.Where(ch => ch.ForumId == user.KfId).ToList();
        
        if (userChannels.Count == 0 && userPartiChannels.Count == 0)
        {
            await botInstance.SendChatMessageAsync("You have no streams.", true);
            return;
        }
        var streamList = userChannels.Aggregate(string.Empty, (current, stream) => current + $"[br]- https://kick.com/{stream.ChannelSlug}");
        foreach (var stream in userPartiChannels)
        {
            var url = $"https://parti.com/creator/{stream.SocialMedia}/{stream.Username}/";
            if (stream.SocialMedia == "discord")
            {
                url += "0";
            }

            streamList += $"[br]- {url}";
        }

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