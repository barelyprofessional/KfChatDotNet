﻿using System.Net;
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
        var reference = await botInstance.SendChatMessageAsync($"{msg} {i}", true);
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
    public Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        throw new Exception("Caused by the test exception command");
    }
}

public class LengthLimitTestCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^test lengthlimit$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Admin;
    // Increased timeout as it has to wait for Sneedchat to echo the message and that can be slow sometimes
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var niceTruncation = await botInstance.SendChatMessageAsync("The quick brown fox jumps over the lazy dog.",
            true, ChatBot.LengthLimitBehavior.TruncateNicely, 20);
        var exactTruncation = await botInstance.SendChatMessageAsync("The quick brown fox jumps over the lazy dog.",
            true, ChatBot.LengthLimitBehavior.TruncateExactly, 20);
        // Would normally get eaten but because we artificially lowered the length limit, it should get sent as-is
        var doNothing = await botInstance.SendChatMessageAsync("The quick brown fox jumps over the lazy dog.",
            true, ChatBot.LengthLimitBehavior.DoNothing, 20);
        var refuseToSend = await botInstance.SendChatMessageAsync("The quick brown fox jumps over the lazy dog.",
            true, ChatBot.LengthLimitBehavior.RefuseToSend, 20);
        await Task.Delay(TimeSpan.FromSeconds(5), ctx);
        logger.Info($"niceTruncation => {niceTruncation.Status}; exactTruncation => {exactTruncation.Status}; doNothing => {doNothing.Status}; refuseToSend => {refuseToSend.Status}");
        if (niceTruncation.ChatMessageId != null)
            botInstance.KfClient.DeleteMessage(niceTruncation.ChatMessageId.Value);
        if (exactTruncation.ChatMessageId != null)
            botInstance.KfClient.DeleteMessage(exactTruncation.ChatMessageId.Value);
        if (doNothing.ChatMessageId != null)
            botInstance.KfClient.DeleteMessage(doNothing.ChatMessageId.Value);
        // Should never happen
        if (refuseToSend.ChatMessageId != null)
            botInstance.KfClient.DeleteMessage(refuseToSend.ChatMessageId.Value);
    }
}