using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Zalgo;

namespace KfChatDotNetBot.Commands;

public class InsanityCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^insanity")];
    public string? HelpText => "Insanity";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("definition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEFdefinition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEF");
    }
}

public class TwistedCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^twisted")];
    public string? HelpText => "Get it twisted";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
    }
}

public class HelpMeCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^helpme")];
    public string? HelpText => "Somebody please help me";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("[img]https://i.postimg.cc/fTw6tGWZ/ineedmoneydumbfuck.png[/img]", true);
    }
}

public class SentCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^sent$")];
    public string? HelpText => "Sent love";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("[img]https://i.ibb.co/GHq7hb1/4373-g-N5-HEH2-Hkc.png[/img]", true);
    }
}

public class GmKasinoCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^gmkasino")];
    public string? HelpText => "Good Morning Kasino";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var images = (await Helpers.GetValue(BuiltIn.Keys.BotGmKasinoImageRotation)).JsonDeserialize<List<string>>();
        if (images == null) return;
        var random = new Random();
        var image = images[random.Next(images.Count)];
        botInstance.SendChatMessage($"[img]{image}[/img]", true);
    }
}

public class CrackedCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^cracked (?<msg>.+)")];
    public string? HelpText => "Crackhead Zalgo text";
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var msg = arguments["msg"].Value.TrimStart('/');
        var settings = await Helpers.GetMultipleValues([
            BuiltIn.Keys.CrackedZalgoFuckUpMode, BuiltIn.Keys.CrackedZalgoFuckUpPosition
        ]);
        var zalgo = new ZalgoString(msg, (FuckUpMode)settings[BuiltIn.Keys.CrackedZalgoFuckUpMode].ToType<int>(),
            (FuckUpPosition)settings[BuiltIn.Keys.CrackedZalgoFuckUpPosition].ToType<int>());
        botInstance.SendChatMessage(zalgo.ToString(), true);
    }
}