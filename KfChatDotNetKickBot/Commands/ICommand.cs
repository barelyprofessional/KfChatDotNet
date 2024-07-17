using System.Text.RegularExpressions;
using KfChatDotNetKickBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetKickBot.Commands;

internal interface ICommand
{
    List<Regex> Patterns { get; }
    string HelpText { get; }
    bool HideFromHelp { get; }
    UserRight RequiredRight { get; }

    Task RunCommand(KickBot botInstance, MessageModel message, GroupCollection arguments, CancellationToken ctx);
}