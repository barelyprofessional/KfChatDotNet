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

public class KasinoMines : IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Task? _minesTimerTask;
    private IDatabase? _redisDb;
    private static ChatBot _kfChatBot;
    private CancellationToken _ct;
    private CancellationTokenSource _minesCts = new();
    public Dictionary<int, KasinoMinesGame>? activeGames;
    private decimal HOUSE_EDGE = (decimal)0.98; //used to rig win rate, payout is 100% fair. with shop i plan to implement a sort of kasino shop profile holding the investments and buffs and tracking the gamblers current house edge
    public class KasinoMinesGame
    {
        public GamblerDbModel creator { get; set; }
        public DateTime lastInteracted = DateTime.UtcNow;
        public char[,] minesBoard;
        public decimal wager { get; set; }
        public int size { get; set; }
        public int mines { get; set; }
        public List<(int r, int c)> betsPlaced = new();
        public SentMessageTrackerModel? lastMessage;
        

        public KasinoMinesGame(GamblerDbModel creator, decimal wager, int size, int mines)
        {
            this.creator = creator;
            this.size = size;
            this.mines = mines;
            this.wager = wager;
            minesBoard = CreateBoard();
        }

        public async Task ResetMessage(SentMessageTrackerModel msg)
        {
            await _kfChatBot.KfClient.DeleteMessageAsync(lastMessage.ChatMessageId.Value);
            lastMessage = msg;
        }

        public async Task RigBoard((int r, int c) coord) //moves one of the mines to a specified coordinate for house edge rigging
        {
            //find the first mine
            (int r, int c) originalMine = (11, 11);
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (minesBoard[r, c] == 'M') originalMine = (r, c);
                }
            }

            minesBoard[coord.r, coord.c] = 'M';
            if (originalMine.r == 11)
            {
                _logger.Error("Rigboard failed to find a mine somehow?");
                return;
            }
            minesBoard[originalMine.r, originalMine.c] = 'G';

        }
        public async Task Explode((int r, int c) mineLocation, SentMessageTrackerModel msg)
        {
            if (lastMessage != msg)
            {
                await ResetMessage(msg);
            }
            int frames = mineLocation.c;
            if (size - mineLocation.c > frames) frames = size - mineLocation.c;
            string str;
            bool revealedSpace;
            int yellowWave = 1;
            int orangeWave = 2;
            int redWave = 3;
            int whiteWave = 0;
            for (int f = 0; f < frames; f++)
            {
                str = "";
                for (int r = 0; r < size; r++)
                {
                    await Task.Delay(100);
                    revealedSpace = false;
                    for (int c = 0; c < size; c++)
                    {
                        foreach (var bet in betsPlaced)
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
                }

                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId.Value, $"{str}[br]{creator.User.FormatUsername()}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
            await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageId.Value);

            (int vertical, int horizontal) DistanceFromMine((int r, int c) coord)
            {
                return (Math.Abs(coord.r - mineLocation.r), Math.Abs(coord.c - mineLocation.c));
            }
        }
        
        public string ToString()
        {
            string value = "";
            bool revealedSpace;
            for (int r = 0; r < size; r++)
            {
                revealedSpace = false;
                for (int c = 0; c < size; c++)
                {
                    foreach (var bet in betsPlaced)
                    {
                        if (bet.r == r && bet.c == c) revealedSpace = true;
                    }

                    if (!revealedSpace)
                    {
                        value += "⬜";
                    }
                    else if (minesBoard[r, c] == 'M') value += "💣";
                    else value += "💎";
                }

                value += "[br]";
            }

            value += $"{creator.User.FormatUsername()}";
            return value;
        }
        
        public char[,] CreateBoard()
        {
            char[,] board = new char[size, size];
            List<(int r, int c)> minesCoords = new List<(int r, int c)>();
            (int r, int c) coord;
            int counter = 0;
            bool gems = !(mines < (size * size)/2); //if there are more mines than gems, generate list of gem locations instead since thats less generations
            int coordsCounter;
            if (gems) coordsCounter = size * size - mines;
            else coordsCounter = mines;
            while (minesCoords.Count != coordsCounter)
            {
                coord = (Money.GetRandomNumber(creator, 0, size), Money.GetRandomNumber(creator, 0, size));
                if (!minesCoords.Contains(coord)) minesCoords.Add(coord);
                else counter++;
                if (counter >= 100000) throw new Exception($"mines failed to generate mines coordinates. Mines: {mines} | Board size: {size} | Current count of mines list {minesCoords.Count}");
            }

            foreach (var coords in minesCoords)
            {
                if (gems) board[coords.r, coords.c] = 'G';
                else board[coords.r, coords.c] = 'M';
            }
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
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
        public async Task DeleteMessage(SentMessageTrackerModel msg)
        {
            await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageId.Value);
        }
    }
    
    public KasinoMines(ChatBot kfChatBot, CancellationToken ct = default)
    {
        _kfChatBot = kfChatBot;
        _ct = ct;
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            _logger.Error($"Can't initialize the Kasino Mines service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            return;
        }

        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
    }

    public async Task RefreshGameMessage(int gamblerId)
    {
        await GetSavedGames();
        var game = activeGames[gamblerId];
        game.lastInteracted = DateTime.UtcNow;
        var msg = await _kfChatBot.SendChatMessageAsync($"{game.ToString()}", true);
        await game.ResetMessage(msg);
        activeGames[gamblerId] = game;
        await SaveActiveGames();
    }
    
    public async Task GetSavedGames()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var json = await _redisDb.StringGetAsync("Mines.State");
        if (string.IsNullOrEmpty(json)) return;
        activeGames = JsonSerializer.Deserialize<Dictionary<int, KasinoMinesGame>>(json.ToString());
        if (activeGames == null)
        {
            _logger.Error("Potentially failed to deserialize active mines games in GetSavedGames() in KasinoMines in Services");
            activeGames = new Dictionary<int, KasinoMinesGame>();
        }
    }
    public async Task SaveActiveGames()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var json = JsonSerializer.Serialize(activeGames);
        await _redisDb.StringSetAsync("Mines.State", json, null, When.Always);
    }
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public async Task RemoveGame(int gamblerId)
    {
        await GetSavedGames();
        activeGames?.Remove(gamblerId);
        await SaveActiveGames();
    }

    public async Task Cashout(KasinoMinesGame game)
    {
        decimal payout = 0;
        decimal possiblePicks = game.size * game.size - game.mines;
        for (int i = 0; i < game.betsPlaced.Count; i++)
        {
            payout += game.wager * (possiblePicks / game.betsPlaced.Count);
            possiblePicks--;
        }

        var newBalance = await Money.NewWagerAsync(game.creator.Id, game.wager, payout, WagerGame.Mines);
        await _kfChatBot.SendChatMessageAsync(
            $"{game.creator.User.FormatUsername()}, you won {payout.FormatKasinoCurrencyAsync()} from your {game.wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.betsPlaced.Count} gems while avoiding {game.mines} mines. Net: {(payout - game.wager).FormatKasinoCurrencyAsync()}. Balance: {newBalance.FormatKasinoCurrencyAsync()}");
        await RemoveGame(game.creator.Id);
    }
        
    public async Task<bool> Bet(int gamblerId, int count, SentMessageTrackerModel msg, bool cashOut = false) //returns false if you hit a bomb, true if you didn't
    {
        await GetSavedGames();
        var game = activeGames[gamblerId];
        game.lastInteracted = DateTime.UtcNow;
        if (game.lastMessage != msg)
        {
            await game.ResetMessage(msg);
        }
        List<(int r, int c)> betCoords = new();
        (int r, int c) coord;
        while (betCoords.Count != count)//creates a list of coordinates to bet on using the coordinate bet function
        {
            coord = (Money.GetRandomNumber(game.creator, 0, game.size), Money.GetRandomNumber(game.creator, 0, game.size));
            if (!betCoords.Contains(coord) && !game.betsPlaced.Contains(coord)) betCoords.Add(coord); 
        }

        return await Bet(gamblerId, betCoords, msg, cashOut);
    }

    public async Task<bool> Bet(int gamblerId, List<(int r, int c)> coords, SentMessageTrackerModel msg, bool cashOut = false)
    {
        await GetSavedGames();
        var game = activeGames[gamblerId];
        game.lastInteracted = DateTime.UtcNow;
        if (game.lastMessage != msg)
        {
            await game.ResetMessage(msg);
        }
        foreach (var coord in coords) //the main portion of the game
        {
            await Task.Delay(100);
            if (game.minesBoard[coord.r, coord.c] == 'M')
            {
                game.betsPlaced.Add(coord);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
                game.Explode((coord.r, coord.c), msg);
                var newBalance = await Money.NewWagerAsync(game.creator.Id, game.wager, -game.wager, WagerGame.Mines);
                await _kfChatBot.SendChatMessageAsync(
                    $"{game.creator.User.FormatUsername()}, you lost your {game.wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.betsPlaced.Count} gems until you hit one of {game.mines} mines. Net: {(-game.wager).FormatKasinoCurrencyAsync()}. Balance: {newBalance.FormatKasinoCurrencyAsync()}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(15));
                await RemoveGame(gamblerId);
                return false;
            }
            
            if (Money.GetRandomNumber(game.creator, 0, 100) < 100 * HOUSE_EDGE)//if you didn't lose, check to see if the switch was flipped
            {
                game.betsPlaced.Add(coord);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());   
                await game.RigBoard(coord);
                await Task.Delay(50);
                await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
                game.Explode(coord, msg);
                var newBalance = await Money.NewWagerAsync(game.creator.Id, game.wager, -game.wager, WagerGame.Mines);
                await _kfChatBot.SendChatMessageAsync(
                    $"{game.creator.User.FormatUsername()}, you lost your {game.wager.FormatKasinoCurrencyAsync()} bet on mines, collecting {game.betsPlaced.Count} gems until you hit one of {game.mines} mines. Net: {(-game.wager).FormatKasinoCurrencyAsync()}. Balance: {newBalance.FormatKasinoCurrencyAsync()}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(15));
            }
            else
            {
                game.betsPlaced.Add(coord);
            }
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, game.ToString());
        }


        activeGames[gamblerId] = game;
        if (cashOut) await Cashout(game);
        else await SaveActiveGames();
        return true;
    }
    
    public bool IsInitialized()
    {
        return _redisDb != null;
    }

    public async Task CreateGame(GamblerDbModel gambler, decimal bet, int size, int mines)
    {
        await GetSavedGames();
        activeGames?.Add(gambler.Id, new KasinoMinesGame(gambler, bet, size, mines));
        await SaveActiveGames();
    }

}



