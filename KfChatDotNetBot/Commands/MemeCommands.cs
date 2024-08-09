using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

public class InsanityCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^insanity")];
    public string HelpText => "Insanity";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("definition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEFdefinition of insanity = doing the same thing over and over and over excecting a different result, and heres my dumbass trying to get rich every day and losing everythign i fucking touch every fucking time FUCK this bullshit FUCK MY LIEF");
    }
}

public class TwistedCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^twisted")];
    public string HelpText => "Get it twisted";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("🦍 🗣 GET IT TWISTED 🌪 , GAMBLE ✅ . PLEASE START GAMBLING 👍 . GAMBLING IS AN INVESTMENT 🎰 AND AN INVESTMENT ONLY 👍 . YOU WILL PROFIT 💰 , YOU WILL WIN ❗ ️. YOU WILL DO ALL OF THAT 💯 , YOU UNDERSTAND ⁉ ️ YOU WILL BECOME A BILLIONAIRE 💵 📈 AND REBUILD YOUR FUCKING LIFE 🤯");
    }
}

public class HelpMeCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^helpme")];
    public string HelpText => "Somebody please help me";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("[img]https://i.postimg.cc/fTw6tGWZ/ineedmoneydumbfuck.png[/img]", true);
    }
}

public class SentCommand : ICommand
{
    public List<Regex> Patterns => [new Regex("^sent$")];
    public string HelpText => "Sent love";
    public bool HideFromHelp => false;
    public UserRight RequiredRight => UserRight.Guest;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        // ReSharper disable once StringLiteralTypo
        botInstance.SendChatMessage("[img]https://i.ibb.co/GHq7hb1/4373-g-N5-HEH2-Hkc.png[/img]", true);
    }
}