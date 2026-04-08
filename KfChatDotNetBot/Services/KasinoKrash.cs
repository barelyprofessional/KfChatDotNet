using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Services;

public class KasinoKrash : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Task? _krashTimerTask;
    private IDatabase? _redisDb;
    private ChatBot _kfChatBot;
    private CancellationToken _ct;
    public KasinoKrashModel? TheGame;

    
    public KasinoKrash(ChatBot kfChatBot, CancellationToken ct = default) //the service itself
    {
        _kfChatBot = kfChatBot;
        _ct = ct;
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            _logger.Error($"Can't initialize the Kasino Krash service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            return;
        }

        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
        //attempt to pull a game from the db in case the bot crashed while a game was ongoing. if so it will restart the run 
        TheGame = GetKrashState().Result;
        if (TheGame != null) _ = RunGame();
    }
    public bool IsInitialized()
    {
        return _redisDb != null;
    }
    public async Task<KasinoKrashModel?> GetKrashState()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Krash service isn't initialized");
        var json = await _redisDb.StringGetAsync("Krash.State");
        if (string.IsNullOrEmpty(json)) return null;
        var data = JsonSerializer.Deserialize<KasinoKrashModel>(json.ToString());
        return data;
    }
    public async Task RemoveKrashState()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino krash service isn't initialized");
        await _redisDb.KeyDeleteAsync("Krash.State");
        TheGame = null;
    }
    public async Task SaveKrashState(KasinoKrashModel krash)
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Krash service isn't initialized");
        var json = JsonSerializer.Serialize(krash);
        await _redisDb.StringSetAsync("Krash.State", json, null, When.Always);
    }

    public async Task AttemptKrash(GamblerDbModel gambler)
    {
        if (TheGame == null)
        {
            throw new InvalidOperationException("Failed to retrieve state or no krash is in progress");
        }
        if (TheGame.Bets.All(x => x.Gambler.User.KfId != gambler.User.KfId)) return;
        if (!TheGame.KrashAccepted) return;
        
        //find which bet is yours
        var index = TheGame.Bets.TakeWhile(bet => bet.Gambler.User.KfId != gambler.User.KfId).Count();
        
        var krashBet = TheGame.Bets[index];
        TheGame.Bets.RemoveAt(index);
        var payout = TheGame.CurrentMulti * krashBet.Wager - krashBet.Wager;
        var newBalance = await Money.NewWagerAsync(krashBet.Gambler.Id, krashBet.Wager, payout, WagerGame.Krash, ct: _ct);
        await _kfChatBot.SendChatMessageAsync(
            $"{krashBet.Gambler.User.FormatUsername()}, you [color=limegreen][b]won[/b][/color] {await payout.FormatKasinoCurrencyAsync()}!",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        if (_kfChatBot.BotServices.KasinoShop != null)
        {
            await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(krashBet.Gambler, WagerGame.Krash, krashBet.Wager,
                payout, newBalance);
        }
        await SaveKrashState(TheGame);
    }
    
    public async Task AddParticipant(GamblerDbModel gambler, decimal wager, decimal multi = -1)
    {
        if (TheGame == null)
        {
            TheGame = await GetKrashState();
            if (TheGame == null) throw new InvalidOperationException("Failed to retrieve state or no krash is in progress");
            _ = RunGame();
        }
        if (TheGame.Bets.Any(x => x.Gambler.User.KfId == gambler.User.KfId)) return;
        if (!TheGame.BetsAccepted) return;
        var bet = new KrashBet{Gambler = gambler, Wager = wager, Multi = multi};
        TheGame.Bets.Add(bet);
        await SaveKrashState(TheGame);
    }

    public async Task StartGame(GamblerDbModel creator, decimal wager, decimal multi = -1)
    {
        TheGame = new KasinoKrashModel(creator);
        TheGame.Bets.Add(new KrashBet{Gambler = creator, Wager = wager, Multi = multi});
        await SaveKrashState(TheGame);
        _ = RunGame();
    }
    public async Task RunGame() //running the actual game
    {
        if (TheGame == null)
        {
            await RemoveKrashState();
            await _kfChatBot.SendChatMessageAsync("Krash error 1", true);
            return;
        }
        var msg = await _kfChatBot.SendChatMessageAsync(
            $"{TheGame.Creator.User.FormatUsername()} started a Krash! You have 30 seconds to place your bets.", true);
        var preGameTimer = TimeSpan.FromSeconds(30);
        var interval = TimeSpan.FromSeconds(1);
        var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(_ct)) //timer before starting the game
        {
            var bets = "";
            foreach (var bet in TheGame.Bets) bets += $"{bet.Gambler.User.FormatUsername()} is betting {bet.Wager}[br]";
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid,
                $"{TheGame.Creator.User.FormatUsername()} started a Krash! You have {preGameTimer} to place your bets.[br]{bets}");
            preGameTimer -= interval;
            if (preGameTimer <= TimeSpan.Zero)
            {
                break;
            }
        }
        
        //any bets placed after this point will be cancelled, must wait until the last game finishes to start a new one.
        TheGame.BetsAccepted = false;
        await SaveKrashState(TheGame);
        
        //start the display of the game
        
        //change these to change the speed of the game
        var growthRate = 1.02m;
        var growthAcceleration = 1.00185m;
        await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageUuid!);
        msg = await _kfChatBot.SendChatMessageAsync($"[center][b][size=200][color=limegreen]{TheGame.CurrentMulti}x");
        var defaultGrowth = 0.01m;
        interval = TimeSpan.FromSeconds(0.1);
        timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(_ct))
        {
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid!, $"[center][b][size=200][color=limegreen]{TheGame.CurrentMulti}x");
            TheGame.CurrentMulti += defaultGrowth;
            defaultGrowth *= growthRate;
            growthRate *= growthAcceleration;
            if (TheGame.CurrentMulti >= TheGame.FinalMulti) break;
        }
        //at this point the game crashes and everybody who did not cash out or pre bet on a multi will have balance subtracted, winners will be paid out.
        await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid!, $"[center][b][size=200][color=red]{TheGame.FinalMulti}x");
        foreach (var bet in TheGame.Bets)
        {
            if (bet.Multi <= TheGame.FinalMulti && bet.Multi != -1)
            {
                //you win
                var payout = TheGame.CurrentMulti * bet.Wager - bet.Wager;
                var newBalance = await Money.NewWagerAsync(bet.Gambler.Id, bet.Wager, payout, WagerGame.Krash, ct: _ct);
                await _kfChatBot.SendChatMessageAsync(
                    $"{bet.Gambler.User.FormatUsername()}, you [color=limegreen][b]won[/b][/color] {await payout.FormatKasinoCurrencyAsync()}!",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                if (_kfChatBot.BotServices.KasinoShop != null)
                {
                    await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(bet.Gambler, WagerGame.Krash, bet.Wager,
                        payout, newBalance);
                }
            }
            else
            {
                //automatically lose, no pre entered multi or it was greater than the final multi and failed to cash out
                var newBalance = await Money.NewWagerAsync(bet.Gambler.Id, bet.Wager, -bet.Wager, WagerGame.Krash, ct: _ct);
                await _kfChatBot.SendChatMessageAsync(
                    $"{bet.Gambler.User.FormatUsername()}, you [color=red][b]lost[/b][/color] {await bet.Wager.FormatKasinoCurrencyAsync()}!",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                if (_kfChatBot.BotServices.KasinoShop != null)
                {
                    await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(bet.Gambler, WagerGame.Krash, bet.Wager,
                        -bet.Wager, newBalance);
                }
            }
        }
        
        //now close the game
        await Task.Delay(5000);
        await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageUuid!);
        await RemoveKrashState();
    }

    
    
    public class KasinoKrashModel
    {
        public GamblerDbModel Creator;
        public decimal FinalMulti = 0;
        public decimal CurrentMulti = 1.01m;
        public List<KrashBet> Bets = new();
        public decimal HouseEdge = 0.98m;
        public bool BetsAccepted = true;
        public bool KrashAccepted = false;
        public KasinoKrashModel(GamblerDbModel creator)
        {
            this.Creator = creator;
            FinalMulti = GetLinearWeightedRandom(1.01, 25000);
        }
        
        private decimal GetLinearWeightedRandom(double minValue, double maxValue)
        {
            var random = RandomShim.Create(StandardRng.Create());
            var r = random.NextDouble(); // Returns 0.0 to 1.0

            // The core 1/x logic
            var result = 1.0 / (1.0 - r);

            // Clamp the result to your specific range
            if (result < minValue) result = minValue;
            if (result > maxValue) result = maxValue;

            return (decimal)result;
        }
    }

    public class KrashBet
    {
        public required GamblerDbModel Gambler{ get; set;}
        public required decimal Wager { get; set; }
        public required decimal Multi { get; set; }
    }
    public void Dispose()
    {
        _krashTimerTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

