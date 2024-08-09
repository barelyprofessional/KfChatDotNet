using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

internal interface ICommand
{
    List<Regex> Patterns { get; }
    string HelpText { get; }
    bool HideFromHelp { get; }
    UserRight RequiredRight { get; }

    Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx);
}