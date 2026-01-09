using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Commands;

public class EightBallCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^8ball", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "Ask the magic 8-ball a question";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(15)
    };

    private static readonly string[] AnswersYes = [
        "Yes, definitely.",
        "It is certain.",
        "Yes, in due time.",
        "Yes, but only if you believe.",
        "It is decidedly so.",
        "Yes, but be cautious.",
        "Yes, but only if you try hard enough.",
        "Yes, but only if you ask nicely.",
        "Yes, but only if you bribe me.",
        "Without a doubt.",
        "You may rely on it.",
        "As I see it, yes.",
        "Most likely.",
        "Outlook good.",
        "Signs point to yes.",
        "Absolutely.",
        "Certainly.",
        "The stars say yes.",
        "The answer is yes.",
        "Definitely yes.",
        "Yes, yes, yes!",
        "Affirmative.",
        "By all means.",
        "The universe agrees.",
        "Chances are good.",
        "It's a sure thing.",
        "You can count on it.",
        "The future looks bright.",
        "Yes, go for it.",
        "Yes, the answer is clear.",
    ];

    private static readonly string[] AnswersUncertain = [
        "Concentrate and ask again.",
        "Ask again later.",
        "Cannot predict now.",
        "Reply hazy, try again.",
        "Better not tell you now.",
        "It's unclear at the moment.",
        "I'm not sure.",
        "Maybe...",
        "That's a mystery.",
        "The answer is unclear.",
        "It's hard to say.",
        "I'm undecided.",
        "The future is uncertain.",
        "It's anyone's guess.",
        "I can't tell you right now.",
        "The signs are unclear.",
    ];

    private static readonly string[] AnswersNo = [
        "No, absolutely not.",
        "No, never.",
        "No, the answer is no.",
        "No, the universe says no.",
        "No, the stars are not aligned.",
        "My reply is no.",
        "Outlook not so good.",
        "Don't count on it.",
        "My sources say no.",
        "Very doubtful.",
        "Definitely not.",
        "No way.",
        "Not in a million years.",
        "The answer is a resounding no.",
        "Chances are slim to none.",
        "The universe says no.",
        "Negative.",
        "Absolutely not.",
        "I don't think so.",
        "The signs point to no.",
        "Certainly not.",
        "I wouldn't bet on it.",
        "The answer is no.",
        "No chance.",
        "Not likely.",
        "The outlook is bleak."
    ];

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var random = RandomShim.Create(StandardRng.Create());

        var outcome = random.Next(0, 110);

        var response = outcome switch
        {
            < 50 => AnswersYes[random.Next(AnswersYes.Length)],
            < 100 => AnswersNo[random.Next(AnswersNo.Length)],
            _ => AnswersUncertain[random.Next(AnswersUncertain.Length)]
        };

        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, {response}", true);
    }
}