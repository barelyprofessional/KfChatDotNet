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
        new Regex(@"^blackjack (?<action>hit|stand|double|split)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<action>hit|stand|double|split)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!blackjack <amount> or !bj <amount> to start, then !bj hit/stand/double/split";
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
        var deck = BlackjackHelper.CreateDeck(gambler);
        
        var playerHand = new List<Card> { deck[0], deck[2] };
        var dealerHand = new List<Card> { deck[1], deck[3] };
        deck.RemoveRange(0, 4);
        
        // Create game state
        var newGameState = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { playerHand },
            DealerHand = dealerHand,
            Deck = deck,
            HasDoubledDown = new List<bool> { false },
            CurrentHandIndex = 0,
            OriginalWagerAmount = wager
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

        var canSplit = BlackjackHelper.CanSplit(playerHand);
        var splitText = canSplit ? " or [B]!bj split[/B]" : "";
        
        await botInstance.SendChatMessageAsync(
            $"🃏 {user.FormatUsername()} started blackjack with {await wager.FormatKasinoCurrencyAsync()}[br]" +
            $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(playerHand)} = {playerValue}[br]" +
            $"[B]Dealer:[/B] {BlackjackHelper.FormatHand(dealerHand, hideFirstCard: true)}[br]" +
            $"Use [B]!bj hit[/B] or [B]!bj stand[/B]{splitText} to continue",
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
        
        switch (action)
        {
            case "hit":
                await HandleHit(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
                break;
            case "stand":
                await HandleStand(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
                break;
            case "double":
                await HandleDouble(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
                break;
            case "split":
                await HandleSplit(botInstance, user, gambler, activeWager, currentGameState, cleanupDelay, ctx);
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
        
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];
        var handLabel = gameState.PlayerHands.Count > 1 ? $" (Hand {gameState.CurrentHandIndex + 1})" : "";
        
        // Draw card
        var card = gameState.Deck[0];
        gameState.Deck.RemoveAt(0);
        currentHand.Add(card);
        
        var playerValue = BlackjackHelper.CalculateHandValue(currentHand);
        
        if (playerValue > 21)
        {
            // Bust - player loses
            var colors = await SettingsProvider.GetMultipleValuesAsync([
                BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
            ]);
            
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}{handLabel} hit and drew {card}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(currentHand)} = {playerValue}[br]" +
                $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]BUST![/COLOR][/B]",
                true, autoDeleteAfter: cleanupDelay);
            
            // Move to next hand or resolve game
            await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
            return;
        }

        // Auto-stand on 21
        if (playerValue == 21)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}{handLabel} hit and drew {card}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(currentHand)} = {playerValue}[br]" +
                $"[B]Standing on 21[/B]",
                true, autoDeleteAfter: cleanupDelay);
            
            await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
            return;
        }

        if (gameState.HasDoubledDown[gameState.CurrentHandIndex])
        {
            // Auto-stand after double down hit
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}{handLabel} hit and drew {card}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(currentHand)} = {playerValue}",
                true, autoDeleteAfter: cleanupDelay);
            
            await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
            return;
        }
        
        // Continue game
        wager.GameMeta = JsonSerializer.Serialize(gameState);
        await _dbContext.SaveChangesAsync(ctx);
            
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}{handLabel} hit and drew {card}[br]" +
            $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(currentHand)} = {playerValue}[br]" +
            $"Use [B]!bj hit[/B] or [B]!bj stand[/B] to continue",
            true, autoDeleteAfter: cleanupDelay);
    }

    private async Task HandleStand(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var handLabel = gameState.PlayerHands.Count > 1 ? $" (Hand {gameState.CurrentHandIndex + 1})" : "";
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];
        var playerValue = BlackjackHelper.CalculateHandValue(currentHand);
        
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}{handLabel} stands with {BlackjackHelper.FormatHand(currentHand)} = {playerValue}",
            true, autoDeleteAfter: cleanupDelay);
        
        await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
    }

    private async Task HandleDouble(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];
        
        // Check if player can double (only on first action with 2 cards)
        if (currentHand.Count != 2)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only double down on your first action.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check if player has enough balance for double
        if (gambler.Balance < gameState.OriginalWagerAmount)
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
        gameState.HasDoubledDown[gameState.CurrentHandIndex] = true;
        
        await _dbContext.SaveChangesAsync(ctx);
        
        var handLabel = gameState.PlayerHands.Count > 1 ? $" (Hand {gameState.CurrentHandIndex + 1})" : "";
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}{handLabel} doubled down! Wager is now {await wager.WagerAmount.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
        
        // Draw one card and auto-stand
        await HandleHit(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
    }

    private async Task HandleSplit(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];
        
        // Check if player can split
        if (!BlackjackHelper.CanSplit(currentHand))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only split with two cards of the same rank.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check if already split
        if (gameState.PlayerHands.Count > 1)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only split once per game.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check if player has enough balance
        if (gambler.Balance < gameState.OriginalWagerAmount)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have enough balance to split.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Check if deck has enough cards
        if (gameState.Deck.Count < 2)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough cards in deck to split.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        // Perform the split
        var card1 = currentHand[0];
        var card2 = currentHand[1];
        
        var hand1 = new List<Card> { card1, gameState.Deck[0] };
        var hand2 = new List<Card> { card2, gameState.Deck[1] };
        gameState.Deck.RemoveRange(0, 2);
        
        gameState.PlayerHands = new List<List<Card>> { hand1, hand2 };
        gameState.HasDoubledDown = new List<bool> { false, false };
        gameState.CurrentHandIndex = 0;
        
        // Charge for the split
        var additionalWager = gameState.OriginalWagerAmount;
        await Money.ModifyBalanceAsync(gambler.Id, -additionalWager, TransactionSourceEventType.Gambling,
            $"Split down for {wager.Id}", ct: ctx);
        wager.WagerAmount += additionalWager;
        wager.WagerEffect -= additionalWager;
        
        wager.GameMeta = JsonSerializer.Serialize(gameState);
        await _dbContext.SaveChangesAsync(ctx);
        
        var value1 = BlackjackHelper.CalculateHandValue(hand1);
        var value2 = BlackjackHelper.CalculateHandValue(hand2);
        
        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()} split their hand! Total wager: {await wager.WagerAmount.FormatKasinoCurrencyAsync()}[br]" +
            $"[B]Hand 1:[/B] {BlackjackHelper.FormatHand(hand1)} = {value1}[br]" +
            $"[B]Hand 2:[/B] {BlackjackHelper.FormatHand(hand2)} = {value2}[br]" +
            $"Playing Hand 1 - Use [B]!bj hit[/B] or [B]!bj stand[/B]",
            true, autoDeleteAfter: cleanupDelay);
    }

    private async Task MoveToNextHandOrResolve(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        gameState.CurrentHandIndex++;
        
        if (gameState.CurrentHandIndex < gameState.PlayerHands.Count)
        {
            // Move to next hand
            wager.GameMeta = JsonSerializer.Serialize(gameState);
            await _dbContext.SaveChangesAsync(ctx);
            
            var nextHand = gameState.PlayerHands[gameState.CurrentHandIndex];
            var nextValue = BlackjackHelper.CalculateHandValue(nextHand);
            
            await botInstance.SendChatMessageAsync(
                $"Playing Hand {gameState.CurrentHandIndex + 1}[br]" +
                $"[B]Your hand:[/B] {BlackjackHelper.FormatHand(nextHand)} = {nextValue}[br]" +
                $"Use [B]!bj hit[/B] or [B]!bj stand[/B] to continue",
                true, autoDeleteAfter: cleanupDelay);
        }
        else
        {
            // All hands played, dealer plays and resolve
            await PlayDealerAndResolve(botInstance, user, gambler, wager, gameState, cleanupDelay, ctx);
        }
    }

    private async Task PlayDealerAndResolve(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        // Check if all hands busted
        bool allHandsBusted = gameState.PlayerHands.All(hand => BlackjackHelper.CalculateHandValue(hand) > 21);
        
        if (!allHandsBusted)
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
        }
        
        await ResolveGame(botInstance, user, gambler, wager, gameState, false, cleanupDelay, ctx);
    }

    private async Task ResolveGame(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, bool immediateResolution, 
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
        var dealerBlackjack = BlackjackHelper.IsBlackjack(gameState.DealerHand);
        
        decimal totalEffect = 0;
        var colors = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        
        var message = $"🃏 {user.FormatUsername()}'s blackjack game:[br]";
        
        // Process each hand
        for (int i = 0; i < gameState.PlayerHands.Count; i++)
        {
            var hand = gameState.PlayerHands[i];
            var playerValue = BlackjackHelper.CalculateHandValue(hand);
            var playerBlackjack = BlackjackHelper.IsBlackjack(hand);
            var handWager = gameState.OriginalWagerAmount;
            
            var handLabel = gameState.PlayerHands.Count > 1 ? $" {i + 1}" : "";
            message += $"[B]Your hand{handLabel}:[/B] {BlackjackHelper.FormatHand(hand)} = {playerValue}[br]";
            
            decimal handEffect;
            string result;
            
            // Determine outcome for this hand
            if (playerBlackjack && dealerBlackjack)
            {
                handEffect = 0;
                result = $"[B][COLOR=orange]PUSH![/COLOR][/B]";
            }
            else if (playerBlackjack)
            {
                handEffect = handWager * 1.5m;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]BLACKJACK! +{await handEffect.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else if (dealerBlackjack)
            {
                handEffect = -handWager;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]DEALER BLACKJACK! -{await handWager.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else if (playerValue > 21)
            {
                handEffect = -handWager;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]BUST! -{await handWager.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else if (dealerValue > 21)
            {
                handEffect = handWager;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]DEALER BUST! +{await handEffect.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else if (playerValue > dealerValue)
            {
                handEffect = handWager;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value}]WIN! +{await handEffect.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else if (playerValue < dealerValue)
            {
                handEffect = -handWager;
                result = $"[B][COLOR={colors[BuiltIn.Keys.KiwiFarmsRedColor].Value}]LOSE! -{await handWager.FormatKasinoCurrencyAsync()}[/COLOR][/B]";
            }
            else
            {
                handEffect = 0;
                result = $"[B][COLOR=orange]PUSH![/COLOR][/B]";
            }
            
            message += $"{result}[br]";
            totalEffect += handEffect;
        }
        
        message += $"[B]Dealer:[/B] {BlackjackHelper.FormatHand(gameState.DealerHand)} = {dealerValue}[br]";
        
        // Update wager to complete
        wager.IsComplete = true;
        wager.WagerEffect = totalEffect;
        wager.Multiplier = (totalEffect + wager.WagerAmount) / wager.WagerAmount;
        
        // Update balance and create transaction in same context
        await _dbContext.SaveChangesAsync(ctx);
        var balanceAdjustment = totalEffect + wager.WagerAmount;
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, balanceAdjustment, TransactionSourceEventType.Gambling,
            $"Blackjack outcome from wager {wager.Id}", null, ctx);
        
        message += $"[B]Net:[/B] {(totalEffect >= 0 ? "+" : "")}{await totalEffect.FormatKasinoCurrencyAsync()} | Balance: {await newBalance.FormatKasinoCurrencyAsync()}";
        
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