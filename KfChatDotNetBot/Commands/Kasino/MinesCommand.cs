using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;


public class MinesCommand : ICommand
{
    public List<Regex> Patterns => [
        //attempting to continue a game below here
        new Regex(@"^mines (?<betString>.+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),                                                       
        new Regex(@"^mines (?<picks>\d+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),                                                               
        //attempting to start a game below here
        new Regex(@"^mines (?<bet>\d+\.\d+) (?<size>\d+) (?<mines>\d+) (?<betString>.+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),        
        new Regex(@"^mines (?<bet>\d+) (?<size>\d+) (?<mines>\d+) (?<betString>.+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),                          
        new Regex(@"^mines (?<bet>\d+\.\d+) (?<size>\d+) (?<mines>\d+) (?<picks>\d+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),           
        new Regex(@"^mines (?<bet>\d+) (?<size>\d+) (?<mines>\d+) (?<picks>\d+) (?<cashout>cashout|)$", RegexOptions.IgnoreCase),                                 
        //cashout
        new Regex(@"^mines (?<cashout>cashout)$", RegexOptions.IgnoreCase),                                                                     
        //refresh
        new Regex(@"^mines (?<refresh>refresh)$", RegexOptions.IgnoreCase),                                                                                 
        //get info
        new Regex("^mines")                                                                                                                     
    ];
    public string? HelpText => "!mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: https://i.ddos.lgbt/raw/UJ9Dty.html";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    
    private const string betPattern = @"(?<row>\d+),(?<col>\d+)";
    private const string toolUrl = "https://i.ddos.lgbt/raw/Kasino%20Mines%20Interface.html";
    
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(10)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoMinesCleanupDelay, BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor,
            BuiltIn.Keys.KasinoMinesEnabled, BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay
        ]);
        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoMinesCleanupDelay].ToType<int>());
        if (!settings[BuiltIn.Keys.KasinoMinesEnabled].ToBoolean())
        {
            var gameDisabledCleanupDelay= TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, mines is currently disabled.", 
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
        
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        bool cashout = false;
        if (message.Message.Contains("cashout")) cashout = true;
        //check if user has an existing game already
        if (!botInstance.BotServices.KasinoMines.activeGames.ContainsKey(gambler.Id))
        {
            if (arguments.TryGetValue("refresh", out var refresh))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you don't have a game running. !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {toolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            //if there is no game currently running
            if (!arguments.TryGetValue("bet", out var bet))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, not enough arguments. !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {toolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            decimal wager = Convert.ToDecimal(bet.Value);
            if (gambler.Balance < wager)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, your balance is too low. Balance: {gambler.Balance.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            if (!arguments.TryGetValue("size", out var size) || !arguments.TryGetValue("mines", out var mines))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, not enough arguments. !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {toolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }

            int pick = 0;
            List<(int r, int c)> precisePicks = new();
            if (arguments.TryGetValue("picks", out var picks)) //if they are using picks to randomly select squares to reveal
            {
                pick = Convert.ToInt32(picks.Value);
            }
            else if (arguments.TryGetValue("betString", out var betString)) //if they are using precise picks manually or from the tool to select specific squares to reveal
            {
                var matches = Regex.Matches(message.Message, betPattern);
                if (matches.Count == 0 || matches == null) //if invalid bet string
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, invalid bet string. Example: !mines 100 10 10 1,3 1,5 2,6 - or use the tool: {toolUrl}", true, autoDeleteAfter: cleanupDelay);
                    return;
                }
                foreach (Match match in matches)
                {
                    precisePicks.Add((Convert.ToInt32(match.Groups["row"].Value), Convert.ToInt32(match.Groups["col"].Value)));
                }
            }
            else //if they didn't put anything
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, not enough arguments. !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {toolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            int boardSize = Convert.ToInt32(size.Value);
            if (boardSize < 2 || boardSize > 10)
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, board size must be between 2 and 10.",true, autoDeleteAfter: cleanupDelay);
                return;
            }
            int minesCount = Convert.ToInt32(mines.Value);
            if (minesCount < 1 || minesCount > (boardSize * boardSize) - 1)
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, number of mines must be between 1 and {boardSize * boardSize - 1}(boardSize * boardSize - 1).",true, autoDeleteAfter: cleanupDelay);
                return;           
            }
            //at this point all valid values so good to continue making the game
            await botInstance.BotServices.KasinoMines.CreateGame(gambler, wager, boardSize, minesCount);
            var msg = await botInstance.SendChatMessageAsync(
                $"{botInstance.BotServices.KasinoMines.activeGames[gambler.Id].ToString()}", true);
            
            if (pick == 0) //if using coordinates
            {
                var game = botInstance.BotServices.KasinoMines.activeGames[gambler.Id];
                foreach (var coord in precisePicks)
                {
                    if (game.betsPlaced.Contains(coord) || coord.r <= 0 || coord.r > game.size || coord.c <= 0 || coord.c > game.size)
                    {
                        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't place duplicate or invalid bets. Use the tool: {toolUrl}", true, autoDeleteAfter: cleanupDelay);
                        return;
                    }
                }
                await botInstance.BotServices.KasinoMines.Bet(gambler.Id, precisePicks, msg, cashout);
            }
            else //if using picks
            {
                await botInstance.BotServices.KasinoMines.Bet(gambler.Id, pick, msg, cashout);
            }
        }
        else
        {
            //if there is a game already running
            if (arguments.TryGetValue("refresh", out var refresh))
            {
                await botInstance.BotServices.KasinoMines.RefreshGameMessage(gambler.Id);
                return;
            }
            int pick = 0;
            List<(int r, int c)> precisePicks = new();
            if (arguments.TryGetValue("picks", out var picks)) //if they are using picks to randomly select squares to reveal
            {
                pick = Convert.ToInt32(picks.Value);
            }
            else if (arguments.TryGetValue("betString", out var betString)) //if they are using precise picks manually or from the tool to select specific squares to reveal
            {
                var matches = Regex.Matches(message.Message, betPattern);
                if (matches.Count == 0 || matches == null) //if invalid bet string
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, invalid bet string. Example: !mines 100 10 10 1,3 1,5 2,6 - or use the tool: {toolUrl}", true, autoDeleteAfter: cleanupDelay);
                    return;
                }
                foreach (Match match in matches)
                {
                    precisePicks.Add((Convert.ToInt32(match.Groups["row"].Value), Convert.ToInt32(match.Groups["col"].Value)));
                }
            }
            else //if they didn't put anything
            {
                if (cashout)
                {
                    await botInstance.BotServices.KasinoMines.Cashout(botInstance.BotServices.KasinoMines.activeGames[gambler.Id]);
                    return;
                }
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you already have a game running. !mines <picks> to reveal more spaces, !mines cashout to cash out, !mines <bet string> to place precise picks. Tool: {toolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            var msg = await botInstance.SendChatMessageAsync(
                $"{botInstance.BotServices.KasinoMines.activeGames[gambler.Id].ToString()}", true);
            
            if (pick == 0) //if using coordinates
            {
                var game = botInstance.BotServices.KasinoMines.activeGames[gambler.Id];
                foreach (var coord in precisePicks)
                {
                    if (game.betsPlaced.Contains(coord) || coord.r <= 0 || coord.r > game.size || coord.c <= 0 || coord.c > game.size)
                    {
                        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't place duplicate or invalid bets. Use the tool: {toolUrl}", true, autoDeleteAfter: cleanupDelay);
                        return;
                    }
                }
                await botInstance.BotServices.KasinoMines.Bet(gambler.Id, precisePicks, msg, cashout);
                
            }
            else //if using picks
            {
                await botInstance.BotServices.KasinoMines.Bet(gambler.Id, pick, msg, cashout);
            }
            
        }
    }
}
