﻿using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

public class TempEnableDiscordRelayingCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^tempenable discord$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.BotServices.TemporarilyBypassGambaSeshForDiscord = true;
        await botInstance.SendChatMessageAsync("Enjoy Discord messages, stalker child", true);
    }
}

public class TempSuppressGambaMessages : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^nogamba$")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.BotServices.TemporarilySuppressGambaMessages = true;
        await botInstance.SendChatMessageAsync("No more gamba notifs", true);
    }
}

public class EnableGambaMessages : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^yesgamba")
    ];

    public string? HelpText => null;
    public UserRight RequiredRight => UserRight.Guest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        botInstance.BotServices.TemporarilySuppressGambaMessages = false;
        await botInstance.SendChatMessageAsync("Gamba notifs back on the menu", true);
    }
}