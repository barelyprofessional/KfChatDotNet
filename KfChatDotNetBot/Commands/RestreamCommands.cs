using System.Text.RegularExpressions;
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