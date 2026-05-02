using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class KrashBetCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^krash (?<amount>\d+(?:\.\d+)?) (?<multi>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^krash (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^krash", RegexOptions.IgnoreCase)
    ];
    
    public string? HelpText => "!rain <amount> to start a rain, !rain to join all active rains";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(90);
    public RateLimitOptionsModel? RateLimitOptions => null;
    public bool WhisperCanInvoke => false;
    
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user,
        GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings =
            await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KasinoKrashEnabled, BuiltIn.Keys.KasinoKrashCleanupDelay,
                BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay
            ]);
        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoKrashCleanupDelay].ToType<int>());
        
        var krashEnabled = settings[BuiltIn.Keys.KasinoKrashEnabled].ToBoolean();
        if (!krashEnabled)
        {
            var gameDisabledCleanupDelay =
                TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, krash is currently disabled.",
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
        
        if (message is { IsWhisper: false, MessageUuid: not null })
        {
            await botInstance.KfClient.DeleteMessageAsync(message.MessageUuid);
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);

        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        
        if (botInstance.BotServices.KasinoKrash == null)
        {
            await botInstance.SendChatMessageAsync("Krash is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        decimal multi;
        decimal wager;
        if (!arguments.TryGetValue("amount", out var amountGroup))
        {
            //attempt to cash out a currently running game
            await botInstance.BotServices.KasinoKrash.AttemptKrash(gambler);
            return;
        }
        if (!arguments.TryGetValue("multi", out var multiGroup))
        {
            multi = -1;
        }
        else
        {
            multi = Convert.ToDecimal(multiGroup.Value);
        }
        wager = Convert.ToDecimal(amountGroup.Value);
        //decimal wagerLimit = 10;
        if (wager > gambler.Balance)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} is not enough to bet {wager} on krash.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(5));
            return;
        }

        /*if (wager > wagerLimit)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can't bet more than {wagerLimit} on krash during testing.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(5));
            return;
        }*/
        if (botInstance.BotServices.KasinoKrash.TheGame == null)
        {
            //start a new game
            await botInstance.BotServices.KasinoKrash.StartGame(gambler, wager, multi);
        }
        else
        {
            //add to the existing game
            await botInstance.BotServices.KasinoKrash.AddParticipant(gambler, wager, multi);
        }
    }
}
