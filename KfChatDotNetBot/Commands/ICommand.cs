using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Commands;

public interface ICommand
{
    List<Regex> Patterns { get; }
    // Set to null to disable help for a given command
    string? HelpText { get; }
    UserRight RequiredRight { get; }
    TimeSpan Timeout { get; }
    RateLimitOptionsModel? RateLimitOptions { get; }
    [Obsolete(ChatBot.WhispersGoneMessage)]
    bool WhisperCanInvoke { get; }

    Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx);
}