using System.Text.Json;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
namespace KfChatDotNetBot.Commands.Kasino;

public class RainCommand : ICommand
{
    public static class RainManager
    {
        public static Dictionary<int,Rain> rainLobbies { get; } = new(); //dictionary of lobbies with the creators id and the lobby
        public static readonly SemaphoreSlim _lock = new(1, 1);
        public static ChatBot botInstance;

        
        public static async Task<bool> AddParticipant(GamblerDbModel gambler, UserDbModel user)
        {
            await _lock.WaitAsync();
            bool openLobby = false;
            try
            {
                foreach (var lobby in rainLobbies.Values)
                {
                    if (lobby.open)
                    {
                        lobby.AddParticipant(gambler);
                        openLobby = true;
                    }
                    
                }
                
            }
            finally
            {
                _lock.Release();
            }
            return openLobby;
        }

        public static async Task<bool> AddRain(int gamblerId, Rain rain)
        {
            await _lock.WaitAsync();
            try
            {
                if (!rainLobbies.TryGetValue(gamblerId, out var lobby))
                {
                    rainLobbies.Add(gamblerId, lobby);
                }
                else //can't start multiple rains at the same time
                {
                    await botInstance.SendChatMessageAsync(
                        $"{lobby.creator.FormatUsername()}, you can't start multiple rains at the same time.");
                    return false;
                }
            }
            finally
            {
                _lock.Release();
            }

            return true;
        }

        public static async Task PayoutRain(Rain rain)
        {
            if (rain.participants.Count == 0)
            {
                await botInstance.SendChatMessageAsync($"{rain.creator.FormatUsername()} made it rain on nobody.");
                await Money.ModifyBalanceAsync(rain.creatorGambler.Id, -rain.rainAmount, TransactionSourceEventType.Rain);
                return;
            }
            else
            {
                decimal payout = rain.rainAmount / rain.participants.Count;
                string rainParticipants = "";
                int counter = 0;
                foreach (var participant in rain.participants)
                {
                    rainParticipants += (rain.participants.Count > 1 && counter == rain.participants.Count - 1) ? $"and {participant.User.FormatUsername()}" : $"{participant.User.FormatUsername()}, ";
                    await Money.ModifyBalanceAsync(participant.Id, payout, TransactionSourceEventType.Rain);
                    counter++;
                }
                if (rain.participants.Count == 1) rainParticipants = rain.participants[0].User.FormatUsername();
                await botInstance.SendChatMessageAsync(
                    $"{rain.creator.FormatUsername()} made it rain {payout.FormatKasinoCurrencyAsync()} on {rainParticipants}",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(30));
                await Money.ModifyBalanceAsync(rain.creatorGambler.Id, -rain.rainAmount, TransactionSourceEventType.Rain);
                
            }
        }

        public static async Task RemoveRain(Rain rain)
        {
            await _lock.WaitAsync();
            try
            {
                rainLobbies.Remove(rain.creatorGambler.Id);
            }
            finally
            {
                _lock.Release();
            }
        }
        public class Rain
        {
            public List<GamblerDbModel> participants = new List<GamblerDbModel>();
            public UserDbModel creator;
            public GamblerDbModel creatorGambler;
            public DateTime startTime { get; } = DateTime.UtcNow;
            public bool open = true;
            public decimal rainAmount;
            
            public Rain(UserDbModel user, GamblerDbModel gambler, decimal wager)
            {
                creator = user;
                creatorGambler = gambler;
                rainAmount = wager;
                _ = RunRain();
            }

            public void AddParticipant(GamblerDbModel gambler)
            {
                participants.Add(gambler);
            }

            public async Task RunRain()
            {
                var chatTimer = TimeSpan.FromSeconds(5);
                int rainTimer = 60;
                var msgId = await botInstance.SendChatMessageAsync(
                    $"{creator.FormatUsername()} is making it rain with {rainAmount.FormatKasinoCurrencyAsync()}! You have {rainTimer} seconds left to join!", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(rainTimer));
                int num = 0;
                while (msgId.ChatMessageId == null)
                {
                    num++;
                    if (msgId.Status is SentMessageTrackerStatus.NotSending or SentMessageTrackerStatus.Lost) return;
                    if (num > 100) return;
                    await Task.Delay(100);
                }
                for (int i = 0; i < rainTimer / Convert.ToInt32(chatTimer); i++)
                {
                    await Task.Delay(chatTimer);
                    await botInstance.KfClient.EditMessageAsync(msgId.ChatMessageId!.Value,
                        $"{creator.FormatUsername()} is making it rain with {rainAmount.FormatKasinoCurrencyAsync()}! You have {rainTimer} seconds left to join!");
                }
                open = false;
                await PayoutRain(this);
                await RemoveRain(this);
            }
            
            
        }
        
    }

    
    
    public List<Regex> Patterns => [
        new Regex(@"^rain (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^rain (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^rain", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!rain <amount> to start a rain, !rain to join all active rains";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        if (!RainManager.botInstance.Equals(botInstance))
        {
            RainManager.botInstance = botInstance;
        }
        var cleanupDelay = TimeSpan.FromSeconds(30);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (!arguments.TryGetValue("amount", out var amount)) //if you're trying to join a rain
        {
            if (RainManager.rainLobbies.Count == 0) //if there are no lobbies
            {
                botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, there are no rain lobbies currently running. !rain <amount> to start a new rain lobby",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }
            else
            {
                bool success = await RainManager.AddParticipant(gambler, user);
                if (!success)
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, there are no rain lobbies currently running. !rain <amount> to start a new rain lobby",
                        true, autoDeleteAfter: cleanupDelay);
                }
                else
                {

                    string rainCreators = "";
                    await RainManager._lock.WaitAsync();
                    int lobbyCount = 0;
                    try
                    {
                        foreach (var lobby in RainManager.rainLobbies.Values)
                        {
                            rainCreators += (RainManager.rainLobbies.Count > 1 && lobbyCount == RainManager.rainLobbies.Count - 1) ?  $"and {lobby.creator.FormatUsername()}": $"{lobby.creator.FormatUsername()}, ";
                            lobbyCount++;
                        }

                    }
                    finally
                    {
                        RainManager._lock.Release();
                    }

                    string areIs = RainManager.rainLobbies.Count > 1 ? "are" : "is";
                    await botInstance.SendChatMessageAsync(
                        $"{rainCreators} {areIs} making it rain on {user.FormatUsername()}!",true, autoDeleteAfter: cleanupDelay);
                    
                }
            }
        }
        //if you're trying to start the rain
        decimal decAmount = Convert.ToDecimal(amount.Value);
        if (decAmount <= 0)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't make it rain with nothing.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        if (gambler.Balance < decAmount)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your balance ${gambler.Balance} KKK is not enough to make it rain for ${decAmount} KKK.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await RainManager.AddRain(gambler.Id,new RainManager.Rain(user, gambler, decAmount));

    }
}
