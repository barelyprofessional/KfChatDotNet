using System.Net;
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

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Admin;
    // Increased timeout as it has to wait for Sneedchat to echo the message and that can be slow sometimes
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var msg = WebUtility.HtmlDecode(arguments["msg"].Value);
        var iterations = 3;
        var i = 0;
        var delay = 1000;
        var reference = botInstance.SendChatMessage($"{msg} {i}", true);
        while (reference.Status == SentMessageTrackerStatus.WaitingForResponse)
        {
            await Task.Delay(100, ctx);
        }

        if (reference.Status == SentMessageTrackerStatus.NotSending ||
            reference.Status == SentMessageTrackerStatus.Unknown ||
            reference.Status == SentMessageTrackerStatus.ChatDisconnected ||
            reference.Status == SentMessageTrackerStatus.Lost ||
            reference.ChatMessageId == null)
        {
            logger.Error("Either message refused to send due to bot settings or something fucked up getting the message ID");
            return;
        }
        while (i < iterations)
        {
            i++;
            await Task.Delay(delay, ctx);
            botInstance.KfClient.EditMessage(reference.ChatMessageId!.Value, $"{msg} {i}");
        }

        await Task.Delay(delay, ctx);
        botInstance.KfClient.EditMessage(reference.ChatMessageId!.Value, "This message will self destruct in 1 second");
        await Task.Delay(delay, ctx);
        botInstance.KfClient.DeleteMessage(reference.ChatMessageId!.Value);
    }
}

public class TimeoutTestCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^test timeout$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Admin;
    // Increased timeout as it has to wait for Sneedchat to echo the message and that can be slow sometimes
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ctx);
    }
}

public class ExceptionTestCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^test exception$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Admin;
    // Increased timeout as it has to wait for Sneedchat to echo the message and that can be slow sometimes
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        throw new Exception("Caused by the test exception command");
    }
}