using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;
using SixLabors.Fonts;
using StackExchange.Redis;

namespace KfChatDotNetBot.Services;

public class KasinoMines
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private IDatabase? _redisDb;
    private static ChatBot _kfChatBot = null!;
    public Dictionary<int, KasinoMinesGame> ActiveGames = new();
    private decimal HOUSE_EDGE = (decimal)0.98; //used to rig win rate, payout is 100% fair. with shop i plan to implement a sort of kasino shop profile holding the investments and buffs and tracking the gamblers current house edge
    public class KasinoMinesGame
    {
        public GamblerDbModel Creator { get; set; }
        public DateTimeOffset LastInteracted = DateTimeOffset.UtcNow;
        public char[,] MinesBoard;
        public decimal Wager { get; set; }
        public int Size { get; set; }
        public int Mines { get; set; }
        public List<(int r, int c)> BetsPlaced = new();
        public int LastMessageId;
        

        public KasinoMinesGame(GamblerDbModel creator, decimal wager, int size, int mines)
        {
            Creator = creator;
            Size = size;
            Mines = mines;
            Wager = wager;
            MinesBoard = CreateBoard();
        }

        public async Task ResetMessage(SentMessageTrackerModel msg)
        {
            _logger.Info("Resetting message");
            await _kfChatBot.KfClient.DeleteMessageAsync(LastMessageId);
            if (msg.ChatMessageId == null) throw new InvalidOperationException($"ChatMessageId was null for {msg.Reference}");
            LastMessageId = msg.ChatMessageId.Value;
        }

        public async Task RigBoard((int r, int c) coord) //moves one of the mines to a specified coordinate for house edge rigging
        {
            //find the first mine
            (int r, int c) originalMine = (11, 11);
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    if (MinesBoard[r, c] == 'M') originalMine = (r, c);
                }
            }

            MinesBoard[coord.r, coord.c] = 'M';
            if (originalMine.r == 11)
            {
                _logger.Error("Rigboard failed to find a mine somehow?");
                return;
            }
            MinesBoard[originalMine.r, originalMine.c] = 'G';

        }
        public async Task Explode((int r, int c) mineLocation, SentMessageTrackerModel msg)
        {
            if (LastMessageId != msg.ChatMessageId!.Value)
            {
                await ResetMessage(msg);
            }
            int frames = mineLocation.c;
            if (Size - mineLocation.c > frames) frames = Size - mineLocation.c;
            string str;
            bool revealedSpace;
            int yellowWave = 1;
            int orangeWave = 2;
            int redWave = 3;
            int whiteWave = 0;
            for (int f = 0; f < frames; f++)
            {
                str = "";
                for (int r = 0; r < Size; r++)
                {
                    await Task.Delay(100);
                    revealedSpace = false;
                    for (int c = 0; c < Size; c++)
                    {
                        foreach (var bet in BetsPlaced)
                        {
                            if (bet.r == r && bet.c == c) revealedSpace = true;
                        }
                        
                        if (mineLocation.r == r && mineLocation.c == c)
                        {
                            str += "💣";
                        }
                        else if (revealedSpace)
                        {
                            str += "💎";
                        }
                        else if (DistanceFromMine((r, c)).vertical == yellowWave || DistanceFromMine((r, c)).horizontal == yellowWave)
                        {
                            str += "🟨";
                        }
                        else if (DistanceFromMine((r, c)).vertical == orangeWave ||
                                 DistanceFromMine((r, c)).horizontal == orangeWave)
                        {
                            str += "🟧";
                        }
                        else if (DistanceFromMine((r, c)).vertical == redWave ||
                                 DistanceFromMine((r, c)).horizontal == redWave)
                        {
                            str += "🟥";
                        }
                        else if (DistanceFromMine((r, c)).vertical == whiteWave ||
                                 DistanceFromMine((r, c)).horizontal == whiteWave)
                        {
                            str += "⬜";
                        }
                        else
                        {
                            str += "⬜";
                        }
                    }
                    str += "[br]";
                }

                await Task.Delay(100);
                await _kfChatBot.KfClient.EditMessageAsync(LastMessageId, $"{str}[br]{Creator.User.FormatUsername()}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
            await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageId!.Value);

            (int vertical, int horizontal) DistanceFromMine((int r, int c) coord)
            {
                return (Math.Abs(coord.r - mineLocation.r), Math.Abs(coord.c - mineLocation.c));
            }
        }
        
        public new string ToString()
        {
            string value = "";
            bool revealedSpace;
            for (int r = 0; r < Size; r++)
            {
                revealedSpace = false;
                for (int c = 0; c < Size; c++)
                {
                    foreach (var bet in BetsPlaced)
                    {
                        if (bet.r == r && bet.c == c) revealedSpace = true;
                    }

                    if (!revealedSpace)
                    {
                        value += "⬜";
                    }
                    else if (MinesBoard[r, c] == 'M') value += "💣";
                    else value += "💎";
                }

                value += "[br]";
            }

            value += $"{Creator.User.FormatUsername()}";
            return value;
        }
        
        public char[,] CreateBoard()
        {
            char[,] board = new char[Size, Size];
            List<(int r, int c)> minesCoords = new List<(int r, int c)>();
            (int r, int c) coord;
            int counter = 0;
            bool gems = !(Mines < (Size * Size)/2); //if there are more mines than gems, generate list of gem locations instead since thats less generations
            int coordsCounter;
            if (gems) coordsCounter = Size * Size - Mines;
            else coordsCounter = Mines;
            while (minesCoords.Count != coordsCounter)
            {
                coord = (Money.GetRandomNumber(Creator, 0, Size, incrementMaxParam: false), Money.GetRandomNumber(Creator, 0, Size, incrementMaxParam: false));
                if (!minesCoords.Contains(coord)) minesCoords.Add(coord);
                else counter++;
                if (counter >= 100000) throw new Exception($"mines failed to generate mines coordinates. Mines: {Mines} | Board size: {Size} | Current count of mines list {minesCoords.Count}");
            }

            foreach (var coords in minesCoords)
            {
                if (gems) board[coords.r, coords.c] = 'G';
                else board[coords.r, coords.c] = 'M';
            }
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    if (gems)
                    {
                        if (!(board[r,c] == 'G'))  board[r, c] = 'M';
                    }
                    else
                    {
                        if (!(board[r,c] == 'M'))  board[r, c] = 'G';
                    }
                }
            }

            return board;
        }

    }
    
    public KasinoMines(ChatBot kfChatBot)
    {
        _kfChatBot = kfChatBot;
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            _logger.Error($"Can't initialize the Kasino Mines service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            return;
        }

        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
        GetSavedGames().Wait();
    }

    public async Task RefreshGameMessage(int gamblerId)
    {
        await GetSavedGames();
        var game = ActiveGames[gamblerId];
        game.LastInteracted = DateTimeOffset.UtcNow;
        var msg = await _kfChatBot.SendChatMessageAsync($"{game.ToString()}", true);
        await _kfChatBot.WaitForChatMessageAsync(msg);
        await game.ResetMessage(msg);
        ActiveGames[gamblerId] = game;
        await SaveActiveGames();
    }
    
    public async Task GetSavedGames()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var json = await _redisDb.StringGetAsync("Mines.State");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            ActiveGames = JsonSerializer.Deserialize<Dictionary<int, KasinoMinesGame>>(json.ToString()) ??
                          throw new InvalidOperationException();
        }
        catch (Exception e)
        {
            _logger.Error(e);
            _logger.Error("Potentially failed to deserialize active mines games in GetSavedGames() in KasinoMines in Services");
            ActiveGames = new Dictionary<int, KasinoMinesGame>();
        }
    }
    public async Task SaveActiveGames()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var json = JsonSerializer.Serialize(ActiveGames);
        await _redisDb.StringSetAsync("Mines.State", json, null, When.Always);
    }

    public async Task RemoveGame(int gamblerId)
    {
        await GetSavedGames();
        ActiveGames?.Remove(gamblerId);
        await SaveActiveGames();
    }

    public async Task Cashout(KasinoMinesGame game)
    {
        decimal payout = 0;
        decimal possiblePicks = game.Size * game.Size - game.Mines;
        for (int i = 0; i < game.BetsPlaced.Count; i++)
        {
            payout += game.Wager * (possiblePicks / game.BetsPlaced.Count);
            possiblePicks--;
        }

        var newBalance = await Money.NewWagerAsync(game.Creator.Id, game.Wager, payout, WagerGame.Mines);
        var net = payout - game.Wager;
        await _kfChatBot.SendChatMessageAsync(
            $"{game.Creator.User.FormatUsername()}, you won {await payout.FormatKasinoCurrencyAsync()} from your {await game.Wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.BetsPlaced.Count} gems while avoiding {game.Mines} mines. Net: {await net.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}");
        await RemoveGame(game.Creator.Id);
    }
        
    public async Task<bool> Bet(int gamblerId, int count, SentMessageTrackerModel msg, bool cashOut = false) //returns false if you hit a bomb, true if you didn't
    {
        await GetSavedGames();
        var game = ActiveGames[gamblerId];
        game.LastInteracted = DateTimeOffset.UtcNow;
        if (game.LastMessageId != msg.ChatMessageId!.Value)
        {
            await game.ResetMessage(msg);
        }
        List<(int r, int c)> betCoords = new();
        (int r, int c) coord;
        while (betCoords.Count != count)//creates a list of coordinates to bet on using the coordinate bet function
        {
            coord = (Money.GetRandomNumber(game.Creator, 0, game.Size-1), Money.GetRandomNumber(game.Creator, 0, game.Size-1));
            if (!betCoords.Contains(coord) && !game.BetsPlaced.Contains(coord)) betCoords.Add(coord); 
        }

        return await Bet(gamblerId, betCoords, msg, cashOut);
    }

    public async Task<bool> Bet(int gamblerId, List<(int r, int c)> coords, SentMessageTrackerModel msg, bool cashOut = false)
    {
        await GetSavedGames();
        var game = ActiveGames[gamblerId];
        game.LastInteracted = DateTimeOffset.UtcNow;
        if (game.LastMessageId != msg.ChatMessageId!.Value)
        {
            await game.ResetMessage(msg);
        }
        foreach (var coord in coords) //the main portion of the game
        {
            await Task.Delay(100);
            if (game.MinesBoard[coord.r, coord.c] == 'M')
            {
                game.BetsPlaced.Add(coord);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
                _ = game.Explode((coord.r, coord.c), msg);
                var newBalance = await Money.NewWagerAsync(game.Creator.Id, game.Wager, -game.Wager, WagerGame.Mines);
                var net = -game.Wager;
                await _kfChatBot.SendChatMessageAsync(
                    $"{game.Creator.User.FormatUsername()}, you lost your {await game.Wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.BetsPlaced.Count} gems until you hit one of {game.Mines} mines. Net: {await net.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(15));
                await RemoveGame(gamblerId);
                return false;
            }
            
            if (Money.GetRandomNumber(game.Creator, 0, 100) > 100 * HOUSE_EDGE)//if you didn't lose, check to see if the switch was flipped
            {
                game.BetsPlaced.Add(coord);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());   
                await game.RigBoard(coord);
                await Task.Delay(50);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
                _ = game.Explode(coord, msg);
                var newBalance = await Money.NewWagerAsync(game.Creator.Id, game.Wager, -game.Wager, WagerGame.Mines);
                var net = -game.Wager;
                await _kfChatBot.SendChatMessageAsync(
                    $"{game.Creator.User.FormatUsername()}, you lost your {await game.Wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.BetsPlaced.Count} gems until you hit one of {game.Mines} mines. Net: {await net.FormatKasinoCurrencyAsync()}. Balance: {await newBalance.FormatKasinoCurrencyAsync()}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(15));
            }
            else
            {
                game.BetsPlaced.Add(coord);
            }
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
        }


        ActiveGames[gamblerId] = game;
        if (cashOut) await Cashout(game);
        else await SaveActiveGames();
        return true;
    }

    public async Task CreateGame(GamblerDbModel gambler, decimal bet, int size, int mines)
    {
        await GetSavedGames();
        ActiveGames.Add(gambler.Id, new KasinoMinesGame(gambler, bet, size, mines));
        await SaveActiveGames();
    }

}



