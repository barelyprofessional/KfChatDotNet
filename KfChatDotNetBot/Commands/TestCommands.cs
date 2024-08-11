using System.Text.RegularExpressions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;
using NLog;

namespace KfChatDotNetBot.Commands;

public class EditTestCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^test edit (?<msg>.+)")
    ];

    public string HelpText => "Test the editing functionality";
    public bool HideFromHelp => true;
    public UserRight RequiredRight => UserRight.Admin;
    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var msg = arguments["msg"].Value;
        var iterations = 3;
        var i = 0;
        var delay = 1000;
        var reference = botInstance.SendChatMessage($"{msg} {i}", true);
        while (botInstance.GetSentMessageStatus(reference).Status == SentMessageTrackerStatus.WaitingForResponse)
        {
            await Task.Delay(100, ctx);
        }
        
        var status = botInstance.GetSentMessageStatus(reference);
        if (status.Status == SentMessageTrackerStatus.NotSending || status.Status == SentMessageTrackerStatus.Unknown ||
            status.ChatMessageId == null)
        {
            logger.Error("Either message refused to send due to bot settings or something fucked up getting the message ID");
            return;
        }
        while (i < iterations)
        {
            i++;
            await Task.Delay(delay, ctx);
            botInstance.KfClient.EditMessage(status.ChatMessageId!.Value, $"{msg} {i}");
        }

        await Task.Delay(delay, ctx);
        botInstance.KfClient.EditMessage(status.ChatMessageId!.Value, "This message will self destruct in 1 second");
        await Task.Delay(delay, ctx);
        botInstance.KfClient.DeleteMessage(status.ChatMessageId!.Value);
    }
}