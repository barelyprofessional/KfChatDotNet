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
        //cashout
        new Regex(@"^mines\s+cashout$", RegexOptions.IgnoreCase),                                                                     
        //refresh
        new Regex(@"^mines\s+refresh$", RegexOptions.IgnoreCase), 
        //clear - admin only
        new Regex(@"^mines\s+clear$", RegexOptions.IgnoreCase),                                                                 
        //start game with number of picks
        new Regex(@"^mines\s+(?<bet>\d+(?:\.\d+)?)\s+(?<size>\d+)\s+(?<mines>\d+)\s+(?<picks>\d+)(?:\s+(?<cashout>cashout))?$", RegexOptions.IgnoreCase), 
        //start game with coordinate string (must contain comma)
        new Regex(@"^mines\s+(?<bet>\d+(?:\.\d+)?)\s+(?<size>\d+)\s+(?<mines>\d+)\s+(?<betString>\d+,\d+(?:\s+\d+,\d+)*)(?:\s+(?<cashout>cashout))?$", RegexOptions.IgnoreCase),  
        //continue game with number of picks
        new Regex(@"^mines\s+(?<picks>\d+)(?:\s+(?<cashout>cashout))?$", RegexOptions.IgnoreCase), 
        //continue game with coordinate string (must contain comma)
        new Regex(@"^mines\s+(?<betString>\d+,\d+(?:\s+\d+,\d+)*)(?:\s+(?<cashout>cashout))?$", RegexOptions.IgnoreCase),                              
        //get info
        new Regex(@"^mines$", RegexOptions.IgnoreCase)                                                                                                                     
    ];
    public string? HelpText => "!mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: https://i.ddos.lgbt/raw/UJ9Dty.html";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    
    private const string BetPattern = @"(?<row>\d+),(?<col>\d+)";
    private const string ToolUrl = "https://i.ddos.lgbt/raw/Kasino%20Mines%20Interface.html";
    
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 3,
        Window = TimeSpan.FromSeconds(10)
    };

    private KasinoMines? KasinoMines;

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
        KasinoMines = new KasinoMines(botInstance, gambler.Id);
        if (message.Message.Contains("clear"))
        {
            if (user.UserRight == UserRight.Admin || user.UserRight == UserRight.TrueAndHonest)
            {
                KasinoMines.GetSavedGames(gambler.Id);
                KasinoMines.ActiveGames.Clear();
                KasinoMines.SaveActiveGames(gambler.Id);
                return;
            }
            else
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you don't have permission to clear saved games.", true, autoDeleteAfter: cleanupDelay);
                return;
            }
        }
        bool cashout = false;
        if (arguments.TryGetValue("cashout", out var cashOut)||message.Message.Contains("cashout")) cashout = true;
        
        if (!Regex.IsMatch(message.Message, @"\d") && cashout) //if the message has no ints its a cashout attempt
        {
            if (KasinoMines.ActiveGames.ContainsKey(gambler.Id))
            {
                await KasinoMines.Cashout(KasinoMines.ActiveGames[gambler.Id]);
                return;
            }
            else
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you don't have a game running to cash out.", true, autoDeleteAfter: cleanupDelay);
                return;
            }
        }
        
        //check if user has an existing game already
        if (!KasinoMines.ActiveGames.ContainsKey(gambler.Id))
        {
            if (arguments.TryGetValue("refresh", out var refresh))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you tried to refresh but don't have a game running. !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {ToolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            //if there is no game currently running
            if (!arguments.TryGetValue("bet", out var bet))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, not enough arguments(bet+). !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {ToolUrl}",
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

            decimal wagerLimit = 10;
            if (wager > wagerLimit)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, mines is temporarily limited to wagers of {await wagerLimit.FormatKasinoCurrencyAsync()} during testing",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }

            if (wager <= 0)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you have to bet something to play mines.", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            if (!arguments.TryGetValue("size", out var size) || !arguments.TryGetValue("mines", out var mines))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, not enough arguments(mines and or size+). !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {ToolUrl}",
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
                var matches = Regex.Matches(message.Message, BetPattern);
                if (matches.Count == 0 || matches == null) //if invalid bet string
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, invalid bet string. Example: !mines 100 10 10 1,3 1,5 2,6 - or use the tool: {ToolUrl}", true, autoDeleteAfter: cleanupDelay);
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
                    $"{user.FormatUsername()}, not enough arguments(picks or betstring). !mines <bet> <board size> <number of mines> <picks> to play simple mines. !mines <bet> <board size> <number of mines> <betString> for advanced mines. Tool: {ToolUrl}",
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
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, number of mines must be between 1 and {boardSize * boardSize - 1}(size^2 - 1).",true, autoDeleteAfter: cleanupDelay);
                return;           
            }
            //at this point all valid values so good to continue making the game
            await KasinoMines.CreateGame(gambler, wager, boardSize, minesCount);
            var msg = await botInstance.SendChatMessageAsync(
                $"{KasinoMines.ActiveGames[gambler.Id].ToString()}", true);
            var msgSuccess = await botInstance.WaitForChatMessageAsync(msg, ct: ctx);
            if (!msgSuccess) throw new InvalidOperationException("Timed out waiting for the message");
            if (pick == 0) //if using coordinates
            {
                var game = KasinoMines.ActiveGames[gambler.Id];
                foreach (var coord in precisePicks)
                {
                    if (game.BetsPlaced.Contains(coord) || coord.r < 0 || coord.r > game.Size || coord.c < 0 || coord.c > game.Size)
                    {
                        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't place duplicate or invalid bets. Use the tool: {ToolUrl}", true, autoDeleteAfter: cleanupDelay);
                        return;
                    }
                }
                await KasinoMines.Bet(gambler.Id, precisePicks, msg, cashout);
            }
            else //if using picks
            {
                await KasinoMines.Bet(gambler.Id, pick, msg, cashout);
            }
        }
        else
        {
            //if there is a game already running
            if (arguments.TryGetValue("refresh", out var refresh))
            {
                await KasinoMines.RefreshGameMessage(gambler.Id);
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
                if (betString.Value == "cashout" || betString.Value == " cashout")
                {
                    await KasinoMines.Cashout(KasinoMines.ActiveGames[gambler.Id]);
                    return;
                }
                var matches = Regex.Matches(message.Message, BetPattern);
                if (matches.Count == 0 || matches == null) //if invalid bet string
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, invalid bet string. Example: !mines 100 10 10 1,3 1,5 2,6 - or use the tool: {ToolUrl}", true, autoDeleteAfter: cleanupDelay);
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
                    await KasinoMines.Cashout(KasinoMines.ActiveGames[gambler.Id]);
                    return;
                }
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you already have a game running. !mines <picks> to reveal more spaces, !mines cashout to cash out, !mines <bet string> to place precise picks. Tool: {ToolUrl}",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }

            var lastmsg = KasinoMines.ActiveGames[gambler.Id].LastMessageId;
            SentMessageTrackerModel msg;
            if (lastmsg == 0)
            {
                msg = (await botInstance.SendChatMessageAsync($"{KasinoMines.ActiveGames[gambler.Id].ToString()}", true));
                await botInstance.WaitForChatMessageAsync(msg, ct: ctx);
                if (msg.ChatMessageId == null) throw new InvalidOperationException("Timed out waiting for the message");
            }
            else
            {
                msg = botInstance.GetSentMessageStatus(KasinoMines.ActiveGames[gambler.Id].LastMessageReference);
            }
            
            if (pick == 0) //if using coordinates
            {
                var game = KasinoMines.ActiveGames[gambler.Id];
                foreach (var coord in precisePicks)
                {
                    if (game.BetsPlaced.Contains(coord) || coord.r <= 0 || coord.r > game.Size || coord.c <= 0 || coord.c > game.Size)
                    {
                        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't place duplicate or invalid bets. Use the tool: {ToolUrl}", true, autoDeleteAfter: cleanupDelay);
                        return;
                    }
                }
                await KasinoMines.Bet(gambler.Id, precisePicks, msg, cashout);
                
            }
            else //if using picks
            {
                await KasinoMines.Bet(gambler.Id, pick, msg, cashout);
            }
            
        }
    }
}
