using System.Text.Json;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using NLog;
using RandN.Compat;
using RandN;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class BlackjackCommand : ICommand
{
    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(5);
    
    public List<Regex> Patterns => [
        new Regex(@"^blackjack (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^blackjack (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^blackjack (?<action>hit|stand|double)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<action>hit|stand|double)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!blackjack <amount> or !bj <amount> to start, then !bj hit/stand/double";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 5,
        Window = TimeSpan.FromSeconds(20),
        Flags = RateLimitFlags.NoAutoDeleteCooldownResponse
    };

    private ApplicationDbContext _dbContext = new();

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromMilliseconds(
            (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KasinoBlackjackCleanupDelay)).ToType<int>());
        
        // Check if this is a new game or continuing existing game
        if (arguments.TryGetValue("amount", out var amountGroup))
        {
            await StartNewGame(botInstance, user, amountGroup.Value, cleanupDelay, ctx);
            return;
        }

        if (arguments.TryGetValue("action", out var actionGroup))
        {
            await ContinueGame(botInstance, user, actionGroup.Value.ToLower(), cleanupDelay, ctx);
            return;
        }

        throw new InvalidOperationException($"User {user.KfUsername} somehow ran blackjack without an amount or action: {message.MessageRaw}");
    }
    

    private async Task StartNewGame(ChatBot botInstance, UserDbModel user, string amountStr, 
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var wager = Convert.ToDecimal(amountStr);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        
        // Check if user has enough balance
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check for existing incomplete blackjack game
        var existingGame = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(w => w.Gambler.Id == gambler.Id &&
                                      w.Game == WagerGame.Blackjack &&
                                      !w.IsComplete && w.GameMeta != null,
                cancellationToken: ctx);
        
        if (existingGame != null)
        {
            try
            {
                _ = JsonSerializer.Deserialize<BlackjackGameMetaModel>(existingGame.GameMeta!) ??
                    throw new InvalidOperationException();
            }
            catch (Exception e)
            {
                logger.Error($"Caught error when deserializing meta for wager ID {existingGame.Id}");
                logger.Error(e);
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, somehow your previous blackjack game state got messed up. Please try again",
                    true, autoDeleteAfter: cleanupDelay);
                existingGame.IsComplete = true;
                await _dbContext.SaveChangesAsync(ctx);
                throw;
            }
            // Check if game has timed out
            var timeSinceStart = DateTimeOffset.UtcNow - existingGame.Time;
            
            if (timeSinceStart > GameTimeout)
            {
                await ForfeitGame(botInstance, user, gambler, existingGame, cleanupDelay, ctx);
                return;
            }

            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you already have an active blackjack game. Use !bj hit or !bj stand to continue.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
       
        // Create deck and deal initial hands
        var rng = StandardRng.Create();
        var random = RandomShim.Create(rng);
        var deck = BlackjackHelper.CreateDeck(random);
        
        var playerHand = new List<Card> { deck[0], deck[2] };
        var dealerHand = new List<Card> { deck[1], deck[3] };
        deck.RemoveRange(0, 4);
        
        // Create game state
        var newGameState = new BlackjackGameMetaModel
        {
            PlayerHand = playerHand,
            DealerHand = dealerHand,
            Deck = deck,
            HasDoubledDown = false
        };
        
        // Create incomplete wager
        await Money.NewWagerAsync(
            gambler.Id,
            wager,
            -wager, // This will be the effect for incomplete wagers
            WagerGame.Blackjack,
            autoModifyBalance: true,
            gameMeta: newGameState,
            isComplete: false,
            ct: ctx
        );
        
        // Update wager ID in game state
        var createdWager = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(
                w => w.Gambler.Id == gambler.Id && w.Game == WagerGame.Blackjack && !w.IsComplete && w.GameMeta != null,
                cancellationToken: ctx) ?? throw new InvalidOperationException();
        createdWager.GameMeta = JsonSerializer.Serialize(newGameState);
        await _dbContext.SaveChangesAsync(ctx);
        
        // Check for immediate blackjacks
        var playerValue = BlackjackHelper.CalculateHandValue(playerHand);
        var dealerValue = BlackjackHelper.CalculateHandValue(dealerHand);
        var playerBlackjack = BlackjackHelper.IsBlackjack(playerHand);
        var dealerBlackjack = BlackjackHelper.IsBlackjack(dealerHand);
        
        if (playerBlackjack || dealerBlackjack)
        {
            await ResolveGame(botInstance, user, gambler, createdWager, newGameState, true, cleanupDelay, ctx);
            return;
        }
        
        // Display initial game state
        var colors = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        
        await botInstance.SendChatMessageAsync(
            $"🃏 {user.FormatUsername()} started blackjack with {await wager.FormatKasinoCurrencyAsync()}[br]" +
            $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(playerHand)} = {playerValue}[br]" +
            $"[B]Dealer:[/B] {BlackjackHelper.FormatHand(dealerHand, hideFirstCard: true)}[br]" +
            $"Use [B]!bj hit[/B] or [B]!bj stand[/B] to continue",
            true, autoDeleteAfter: cleanupDelay);
    }

    private async Task ContinueGame(ChatBot botInstance, UserDbModel user, string action, 
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        
        // Find active game
        var activeWager = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(w => w.Gambler.Id == gambler.Id &&
                        w.Game == WagerGame.Blackjack &&
                        !w.IsComplete && w.GameMeta != null, cancellationToken: ctx);
        
        if (activeWager == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have an active blackjack game. Start one with !bj <amount>",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        var currentGameState = JsonSerializer.Deserialize<BlackjackGameMetaModel>(activeWager.GameMeta!);
        if (currentGameState == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your game data is corrupted. Please start a new game.",
                true, autoDeleteAfter: cleanupDelay);
            activeWager.IsComplete = true;
            await _dbContext.SaveChangesAsync(ctx);
            return;
        }
        
        // Check timeout
        var timeSinceStart = DateTimeOffset.UtcNow - activeWager.Time;
        if (timeSinceStart > GameTimeout)
        {
            await ForfeitGame(botInstance, user, gambler, activeWager, cleanupDelay, ctx);
            return;
        }
        
        var rng = StandardRng.Create();
        var random = RandomShim.Create(rng);
        
        switch (action)
        {
            case "hit":
                await HandleHit(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
                break;
            case "stand":
                await HandleStand(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
                break;
            case "double":
                await HandleDouble(botInstance, user, gambler, activeWager, currentGameState, random, cleanupDelay, ctx);
                break;
        }
    }

    private async Task HandleHit(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        
        if (gameState.Deck.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, game error: no cards left in deck. Game forfeited.",
                true, autoDeleteAfter: cleanupDelay);
            await ForfeitGame(botInstance, user, gambler, wager, cleanupDelay, ctx);
            return;
        }
        
        // Draw card
        var card = gameState.Deck[0];
        gameState.Deck.RemoveAt(0);
        gameState.PlayerHand.Add(card);
        
        var playerValue = BlackjackHelper.CalculateHandValue(gameState.PlayerHand);
        
        if (playerValue > 21)
        {
            var redColor = (await SettingsProvider.GetValueAsync(BuiltIn.Keys.KiwiFarmsRedColor)).Value;
            // Bust - player loses
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()} hit and drew {card}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(gameState.PlayerHand)} = {playerValue}[br]" +
                $"[B][COLOR={redColor}]BUST![/COLOR][/B]",
                true, autoDeleteAfter: cleanupDelay);
            
            await ResolveGame(botInstance, user, gambler, wager, gameState, false, cleanupDelay, ctx);
            return;
        }

        if (gameState.HasDoubledDown)
        {
            // Auto-stand after double down hit
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()} hit and drew {card}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(gameState.PlayerHand)} = {playerValue}",
                true, autoDeleteAfter: cleanupDelay);
            
            await HandleStand(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
            return;
        }
        
        // Continue game
        wager.GameMeta = JsonSerializer.Serialize(gameState);
        await _dbContext.SaveChangesAsync(ctx);
            
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()} hit and drew {card}[br]" +
            $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(gameState.PlayerHand)} = {playerValue}[br]" +
            $"Use [B]!bj hit[/B] or [B]!bj stand[/B] to continue",
            true, autoDeleteAfter: cleanupDelay);
    }

    private async Task HandleStand(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        
        // Dealer plays
        var dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
        
        while (dealerValue < 17)
        {
            if (gameState.Deck.Count == 0)
            {
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, game error: dealer ran out of cards. Game forfeited.",
                    true, autoDeleteAfter: cleanupDelay);
                await ForfeitGame(botInstance, user, gambler, wager, cleanupDelay, ctx);
                return;
            }
            
            var card = gameState.Deck[0];
            gameState.Deck.RemoveAt(0);
            gameState.DealerHand.Add(card);
            dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
        }
        
        await ResolveGame(botInstance, user, gambler, wager, gameState, false, cleanupDelay, ctx);
    }

    private async Task HandleDouble(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, Random random, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        // Check if player can double (only on first action with 2 cards)
        if (gameState.PlayerHand.Count != 2)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only double down on your first action.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check if player has enough balance for double
        if (gambler.Balance < wager.WagerAmount)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have enough balance to double down.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Double the wager
        var additionalWager = wager.WagerAmount;
        await Money.ModifyBalanceAsync(gambler.Id, -additionalWager, TransactionSourceEventType.Gambling,
            $"Double down for {wager.Id}", ct: ctx);
        wager.WagerAmount *= 2;
        wager.WagerEffect -= additionalWager; // Subtract the additional wager
        gameState.HasDoubledDown = true;
        
        await _dbContext.SaveChangesAsync(ctx);
        
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()} doubled down! Wager is now {await wager.WagerAmount.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
        
        // Draw one card and auto-stand
        await HandleHit(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
    }

    private async Task ResolveGame(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, bool immediateResolution, 
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var playerValue = BlackjackHelper.CalculateHandValue(gameState.PlayerHand);
        var dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
        var playerBlackjack = BlackjackHelper.IsBlackjack(gameState.PlayerHand);
        var dealerBlackjack = BlackjackHelper.IsBlackjack(gameState.DealerHand);
        
        decimal finalEffect;
        decimal multiplier;
        string result;
        
        var colors = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        
        // Determine outcome
        if (playerBlackjack && dealerBlackjack)
        {
            finalEffect = 0;
            multiplier = 1m;
            result = $"[B][COLOR=orange]PUSH![/COLOR][/B] Both have blackjack";
        }
        else if (playerBlackjack)
        {
            finalEffect = wager.WagerAmount * 1.5m;
            multiplier = 2.5m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]BLACKJACK![/COLOR][/B]";
        }
        else if (dealerBlackjack)
        {
            finalEffect = -wager.WagerAmount;
            multiplier = 0m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]DEALER BLACKJACK![/COLOR][/B]";
        }
        else if (playerValue > 21)
        {
            finalEffect = -wager.WagerAmount;
            multiplier = 0m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]BUST![/COLOR][/B]";
        }
        else if (dealerValue > 21)
        {
            finalEffect = wager.WagerAmount;
            multiplier = 2m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]DEALER BUST - YOU WIN![/COLOR][/B]";
        }
        else if (playerValue > dealerValue)
        {
            finalEffect = wager.WagerAmount;
            multiplier = 2m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]YOU WIN![/COLOR][/B]";
        }
        else if (playerValue < dealerValue)
        {
            finalEffect = -wager.WagerAmount;
            multiplier = 0m;
            result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]DEALER WINS![/COLOR][/B]";
        }
        else
        {
            finalEffect = 0;
            multiplier = 1m;
            result = $"[B][COLOR=orange]PUSH![/COLOR][/B]";
        }
        
        // Update wager to complete
        wager.IsComplete = true;
        wager.WagerEffect = finalEffect;
        wager.Multiplier = multiplier;
        await _dbContext.SaveChangesAsync(ctx);
        var balanceAdjustment = finalEffect + wager.WagerAmount;
        await Money.ModifyBalanceAsync(gambler.Id, balanceAdjustment, TransactionSourceEventType.Gambling,
            $"Blackjack outcome from wager {wager.Id}", null, ctx);
        
        // Display result
        var message = $"🃏 {user.FormatUsername()}'s blackjack game:[br]" +
                     $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(gameState.PlayerHand)} = {playerValue}[br]" +
                     $"[B]Dealer:[/B] {BlackjackHelper.FormatHand(gameState.DealerHand)} = {dealerValue}[br]" +
                     $"{result}[br]";
        
        if (finalEffect > 0)
        {
            message += $"You won {await finalEffect.FormatKasinoCurrencyAsync()}! ";
        }
        else if (finalEffect < 0)
        {
            message += $"You lost {await Math.Abs(finalEffect).FormatKasinoCurrencyAsync()}! ";
        }
        
        message += $"Balance: {await gambler.Balance.FormatKasinoCurrencyAsync()}";
        
        await botInstance.SendChatMessageAsync(message, true, autoDeleteAfter: cleanupDelay);
    }

    private async Task ForfeitGame(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        wager.IsComplete = true;
        await _dbContext.SaveChangesAsync(ctx);
        
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, your blackjack game timed out and you forfeited {await wager.WagerAmount.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
    }
}