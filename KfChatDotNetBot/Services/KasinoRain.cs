using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using Microsoft.EntityFrameworkCore;
using NLog;
using StackExchange.Redis;

namespace KfChatDotNetBot.Services;

public class KasinoRain : IDisposable
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private Task? _rainTimerTask;
    private IDatabase? _redisDb;
    private ChatBot _kfChatBot;
    private CancellationToken _ct;
    private CancellationTokenSource _rainCts = new();

    public KasinoRain(ChatBot kfChatBot, CancellationToken ct = default)
    {
        _kfChatBot = kfChatBot;
        _ct = ct;
        var connectionString = SettingsProvider.GetValueAsync(BuiltIn.Keys.BotRedisConnectionString).Result;
        if (string.IsNullOrEmpty(connectionString.Value))
        {
            _logger.Error($"Can't initialize the Kasino Rain service as Redis isn't configured in {BuiltIn.Keys.BotRedisConnectionString}");
            return;
        }

        var redis = ConnectionMultiplexer.Connect(connectionString.Value);
        _redisDb = redis.GetDatabase();
        _rainTimerTask = Task.Run(RainTimerTask, ct);
    }

    public bool IsInitialized()
    {
        return _redisDb != null;
    }

    public async Task AddParticipant(int userId)
    {
        var data = await GetRainState();
        if (data == null) throw new InvalidOperationException("Failed to retrieve state or no rain is in progress");
        if (data.Participants.Contains(userId)) return;
        data.Participants.Add(userId);
        await SaveRainState(data);
    }

    public async Task RemoveRainState()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Rain service isn't initialized");
        await _redisDb.KeyDeleteAsync("Rain.State");
    }

    public async Task<KasinoRainModel?> GetRainState()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Rain service isn't initialized");
        var json = await _redisDb.StringGetAsync("Rain.State");
        if (string.IsNullOrEmpty(json)) return null;
        var data = JsonSerializer.Deserialize<KasinoRainModel>(json.ToString());
        return data;
    }

    public async Task SaveRainState(KasinoRainModel rain)
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino Rain service isn't initialized");
        var json = JsonSerializer.Serialize(rain);
        await _redisDb.StringSetAsync("Rain.State", json, null, When.Always);
    }

    private async Task RainTimerTask()
    {
        var interval = TimeSpan.FromSeconds(1);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(_ct))
        {
            var rain = await GetRainState();
            if (rain == null) continue;
            if (DateTimeOffset.UtcNow < rain.PayoutWhen) continue;
            var creator = await Money.GetGamblerEntityAsync(rain.Creator, ct: _ct);
            if (creator == null)
            {
                _logger.Error($"Somehow this rain was created by a non-existent (or banned) gambler with user ID {rain.Creator}? Wiping this fucked up state");
                await RemoveRainState();
                continue;
            }
            
            if (rain.Participants.Count == 0)
            {
                await _kfChatBot.SendChatMessageAsync(
                    $"Nobody participated in {creator.User.FormatUsername()}'s rain!",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(30));
                await RemoveRainState();
                continue;
            }

            if (rain.RainAmount > creator.Balance)
            {
                await _kfChatBot.SendChatMessageAsync(
                    $"{creator.User.FormatUsername()} lost it all before he could bless everyone! The giveaway is canceled! :lossmanjack:",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(30));
                await RemoveRainState();
                continue;
            }
            
            List<string> participantNames = [];
            var payout = rain.RainAmount / rain.Participants.Count;
            decimal failedPayoutAmount = 0;
            foreach (var participant in rain.Participants)
            {
                var gambler = await Money.GetGamblerEntityAsync(participant, ct: _ct);
                if (gambler == null)
                {
                    _logger.Error($"Somehow this participant ({participant}) does not have a gambler entity or has been banned");
                    failedPayoutAmount += payout;
                    continue;
                }
                participantNames.Add(gambler.User.FormatUsername());
                await Money.ModifyBalanceAsync(gambler.Id, payout, TransactionSourceEventType.Rain,
                    "Payout from rain event", creator.Id, _ct);
            }

            await Money.ModifyBalanceAsync(creator.Id, -rain.RainAmount + failedPayoutAmount, TransactionSourceEventType.Rain,
                $"Rained on {participantNames.Count} people", ct: _ct);
            await _kfChatBot.SendChatMessageAsync(
                $"{creator.User.FormatUsername()} made it rain {await payout.FormatKasinoCurrencyAsync()} on {string.Join(' ', participantNames)}",
                true, autoDeleteAfter: TimeSpan.FromSeconds(30));
            await RemoveRainState();
        }
    }
    public void Dispose()
    {
        _rainTimerTask?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class KasinoRainModel
{
    public required List<int> Participants { get; set; } = [];
    public required int Creator { get; set; }
    public required DateTimeOffset Started { get; set; }
    public required decimal RainAmount { get; set; }
    public required DateTimeOffset PayoutWhen { get; set; }
}
