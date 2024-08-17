﻿using System.Runtime.Caching;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands;

public class CacheClearCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex("^cache clear")
    ];

    public string HelpText => "Clear the cache";
    public bool HideFromHelp => true;
    public UserRight RequiredRight => UserRight.Admin;
    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cacheKeys = MemoryCache.Default.Select(kvp => kvp.Key).ToList();
        foreach (var cacheKey in cacheKeys)
        {
            MemoryCache.Default.Remove(cacheKey);
        }
        botInstance.SendChatMessage("Cache wiped", true);
    }
}