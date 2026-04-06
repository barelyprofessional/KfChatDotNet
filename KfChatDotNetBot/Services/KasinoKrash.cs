using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;
using System.Text.Json.Serialization;
using KfChatDotNetBot.Commands.Kasino;
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
    public decimal HOUSE_EDGE = 0.98m;
    public KasinoKrashModel? theGame;

    
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
        theGame = GetKrashState().Result;
        if (theGame != null) _ = RunGame();
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
        theGame = null;
    }
    public async Task SaveKrashState(KasinoKrashModel krash)
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Krash service isn't initialized");
        var json = JsonSerializer.Serialize(krash);
        await _redisDb.StringSetAsync("Krash.State", json, null, When.Always);
    }

    public async Task AttemptKrash(GamblerDbModel gambler)
    {
        if (theGame == null)
        {
            throw new InvalidOperationException("Failed to retrieve state or no krash is in progress");
        }
        if (!theGame.bets.Any(x => x.gambler.User.KfId == gambler.User.KfId)) return;
        if (!theGame.krashAccepted) return;
        
        //find which bet is yours
        int index = 0;
        foreach (var bet in theGame.bets)
        {
            if (bet.gambler.User.KfId == gambler.User.KfId) break;
            index++;
        }

        var krashBet = theGame.bets[index];
        theGame.bets.RemoveAt(index);
        decimal payout = theGame.currentMulti * krashBet.wager - krashBet.wager;
        var newBalance = await Money.NewWagerAsync(krashBet.gambler.Id, krashBet.wager, payout, WagerGame.Krash);
        await _kfChatBot.SendChatMessageAsync(
            $"{krashBet.gambler.User.FormatUsername()}, you [color=limegreen][b]won[/b][/color] {await payout.FormatKasinoCurrencyAsync()}!",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        if (_kfChatBot.BotServices.KasinoShop != null)
        {
            await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(krashBet.gambler, WagerGame.Krash, krashBet.wager,
                payout, newBalance);
        }
        await SaveKrashState(theGame);
    }
    
    public async Task AddParticipant(GamblerDbModel gambler, decimal wager, decimal multi = -1)
    {
        if (theGame == null)
        {
            theGame = await GetKrashState();
            if (theGame == null) throw new InvalidOperationException("Failed to retrieve state or no krash is in progress");
            _ = RunGame();
        }
        if (theGame.bets.Any(x => x.gambler.User.KfId == gambler.User.KfId)) return;
        if (!theGame.betsAccepted) return;
        KrashBet bet = new KrashBet{gambler = gambler, wager = wager, multi = multi};
        theGame.bets.Add(bet);
        await SaveKrashState(theGame);
    }

    public async Task StartGame(GamblerDbModel creator, decimal wager, decimal multi = -1)
    {
        theGame = new KasinoKrashModel(creator);
        theGame.bets.Add(new KrashBet{gambler = creator, wager = wager, multi = multi});
        await SaveKrashState(theGame);
        _ = RunGame();
    }
    public async Task RunGame() //running the actual game
    {
        if (theGame == null)
        {
            await RemoveKrashState();
            await _kfChatBot.SendChatMessageAsync("Krash error 1", true);
            return;
        }
        var msg = await _kfChatBot.SendChatMessageAsync(
            $"{theGame.creator.User.FormatUsername()} started a Krash! You have 30 seconds to place your bets.", true);
        TimeSpan preGameTimer = TimeSpan.FromSeconds(30);
        TimeSpan interval = TimeSpan.FromSeconds(1);
        var timer = new PeriodicTimer(interval);
        string bets;
        while (await timer.WaitForNextTickAsync(_ct)) //timer before starting the game
        {
            bets = "";
            foreach (var bet in theGame.bets) bets += $"{bet.gambler.User.FormatUsername()} is betting {bet.wager}[br]";
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid,
                $"{theGame.creator.User.FormatUsername()} started a Krash! You have {preGameTimer} to place your bets.[br]{bets}");
            preGameTimer -= interval;
            if (preGameTimer <= TimeSpan.Zero)
            {
                break;
            }
        }
        
        //any bets placed after this point will be cancelled, must wait until the last game finishes to start a new one.
        theGame.betsAccepted = false;
        await SaveKrashState(theGame);
        
        //start the display of the game
        
        //change these to change the speed of the game
        decimal growthRate = 1.02m;
        decimal growthAcceleration = 1.00185m;
        await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageUuid!);
        msg = await _kfChatBot.SendChatMessageAsync($"[center][b][size=200][color=limegreen]{theGame.currentMulti}x");
        decimal defaultGrowth = 0.01m;
        interval = TimeSpan.FromSeconds(0.1);
        timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(_ct))
        {
            await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid!, $"[center][b][size=200][color=limegreen]{theGame.currentMulti}x");
            theGame.currentMulti += defaultGrowth;
            defaultGrowth *= growthRate;
            growthRate *= growthAcceleration;
            if (theGame.currentMulti >= theGame.finalMulti) break;
        }
        //at this point the game crashes and everybody who did not cash out or pre bet on a multi will have balance subtracted, winners will be paid out.
        await _kfChatBot.KfClient.EditMessageAsync(msg.ChatMessageUuid!, $"[center][b][size=200][color=red]{theGame.finalMulti}x");
        foreach (var bet in theGame.bets)
        {
            if (bet.multi <= theGame.finalMulti)
            {
                //you win
                decimal payout = theGame.currentMulti * bet.wager - bet.wager;
                var newBalance = await Money.NewWagerAsync(bet.gambler.Id, bet.wager, payout, WagerGame.Krash);
                await _kfChatBot.SendChatMessageAsync(
                    $"{bet.gambler.User.FormatUsername()}, you [color=limegreen][b]won[/b][/color] {await payout.FormatKasinoCurrencyAsync()}!",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                if (_kfChatBot.BotServices.KasinoShop != null)
                {
                    await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(bet.gambler, WagerGame.Krash, bet.wager,
                        payout, newBalance);
                }
            }
            else
            {
                //automatically lose, no pre entered multi or it was greater than the final multi and failed to cash out
                var newBalance = await Money.NewWagerAsync(bet.gambler.Id, bet.wager, -bet.wager, WagerGame.Krash);
                await _kfChatBot.SendChatMessageAsync(
                    $"{bet.gambler.User.FormatUsername()}, you [color=red][b]lost[/b][/color] {await bet.wager.FormatKasinoCurrencyAsync()}!",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                if (_kfChatBot.BotServices.KasinoShop != null)
                {
                    await _kfChatBot.BotServices.KasinoShop.ProcessWagerTracking(bet.gambler, WagerGame.Krash, bet.wager,
                        -bet.wager, newBalance);
                }
            }
        }
        
        //now close the game
        await _kfChatBot.KfClient.DeleteMessageAsync(msg.ChatMessageUuid!);
        await RemoveKrashState();
    }

    
    
    public class KasinoKrashModel
    {
        public GamblerDbModel creator;
        public decimal finalMulti = 0;
        public decimal currentMulti = 1.01m;
        public List<KrashBet> bets = new();
        public decimal HOUSE_EDGE = 0.98m;
        public bool betsAccepted = true;
        public bool krashAccepted = false;
        public KasinoKrashModel(GamblerDbModel creator)
        {
            this.creator = creator;
            finalMulti = GetLinearWeightedRandom(1.01, 25000);
        }
        
        private decimal GetLinearWeightedRandom(double minValue, double maxValue)
        {
            var random = RandomShim.Create(StandardRng.Create());
            double r = random.NextDouble(); // Returns 0.0 to 1.0

            // The core 1/x logic
            double result = 1.0 / (1.0 - r);

            // Clamp the result to your specific range
            if (result < minValue) result = minValue;
            if (result > maxValue) result = maxValue;

            return (decimal)result;
        }
    }

    public class KrashBet
    {
        public required GamblerDbModel gambler{ get; set;}
        public required decimal wager { get; set; }
        public required decimal multi { get; set; }
    }
    public void Dispose()
    {
        _krashTimerTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

