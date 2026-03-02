using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetWsClient.Models.Events;
namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class RainCommand : ICommand
{
    public List<Regex> Patterns => [
        new Regex(@"^rain (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^rain", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!rain <amount> to start a rain, !rain to join all active rains";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(90);
    public RateLimitOptionsModel? RateLimitOptions => null;

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(30);
        if (botInstance.BotServices.KasinoRain == null || !botInstance.BotServices.KasinoRain.IsInitialized())
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, rain is not available at this time", true,
                autoDeleteAfter: cleanupDelay);
            return;
        }

        var rain = await botInstance.BotServices.KasinoRain.GetRainState();
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (!arguments.TryGetValue("amount", out var amount)) //if you're trying to join a rain
        {
            if (rain == null) //if there are no lobbies
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, there's no rain currently running. !rain <amount> to start a new rain",
                    true, autoDeleteAfter: cleanupDelay);
                return;
            }

            if (rain.Participants.Contains(user.Id))
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you're already participating in this rain!", true,
                    autoDeleteAfter: cleanupDelay);
                return;
            }

            if (rain.Creator == user.Id)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, you can't participate in your own rain!", true,
                    autoDeleteAfter: cleanupDelay);
                return;
            }

            await botInstance.BotServices.KasinoRain.AddParticipant(user.Id);
            var pluralSuffix = string.Empty;
            if (rain.Participants.Count > 0) pluralSuffix = "s";
            await botInstance.SendChatMessageAsync(
                $"LFG {user.FormatUsername()} is now a participant! There's now {rain.Participants.Count + 1} participant{pluralSuffix}! Type [ditto]!rain[/ditto] to participate",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        //if you're trying to start the rain
        if (rain != null)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, there's already a rain in progress.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        decimal decAmount = Convert.ToDecimal(amount.Value);
        if (decAmount <= 0)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you can't make it rain with nothing.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        if (gambler.Balance < decAmount)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance {await gambler.Balance.FormatKasinoCurrencyAsync()} is not enough to make it rain for {await decAmount.FormatKasinoCurrencyAsync()}.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        decimal rainMin = 100;
        if (decAmount < rainMin)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, rain at least {await rainMin.FormatKasinoCurrencyAsync()}", true,
                autoDeleteAfter: cleanupDelay);
            return;
        }

        rain = new KasinoRainModel
        {
            Participants = [],
            Creator = user.Id,
            Started = DateTimeOffset.UtcNow,
            RainAmount = decAmount,
            PayoutWhen = DateTimeOffset.MaxValue
        };
        var timer = 60;
        var msg = await botInstance.SendChatMessageAsync(
            $"🌧️🌧️ {user.FormatUsername()} is making it rain with {await decAmount.FormatKasinoCurrencyAsync()}! Type [ditto]!rain[/ditto] in the next {timer} seconds to join.",
            true);
        var result = await botInstance.WaitForChatMessageAsync(msg, ct: ctx);
        if (!result)
        {
            throw new InvalidOperationException("Failed to send chat message for the rain. Not going to proceed with it");
        }

        // Wait to set a real payout deadline only when chyat echoes the message out of fairness
        // (and also so the timer doesn't overlap with the payout deadline)
        rain.PayoutWhen = DateTimeOffset.UtcNow.AddSeconds(60);
        await botInstance.BotServices.KasinoRain.SaveRainState(rain);
        while (timer > 0)
        {
            timer--;
            await Task.Delay(1000, ctx);
            await botInstance.KfClient.EditMessageAsync(msg.ChatMessageUuid!,
                $"🌧️🌧️ {user.FormatUsername()} is making it rain with {await decAmount.FormatKasinoCurrencyAsync()}! Type [ditto]!rain[/ditto] in the next {timer} seconds to join.");
        }

        await Task.Delay(100, ctx);
        await botInstance.KfClient.DeleteMessageAsync(msg.ChatMessageUuid!);
        // At this point the timer should take care of things but truthfully it's a disaster and probably won't work
    }
}